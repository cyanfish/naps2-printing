using System.Runtime.ExceptionServices;
using NAPS2.Windows.Printing.Interop;

namespace NAPS2.Windows.Printing;

/// <summary>
/// Prints images with the Windows print dialog, which shows a preview of the pages.
/// </summary>
public static class WindowsPrintDialog
{
    private const string PRINT_MANAGER_CLASS = "Windows.Graphics.Printing.PrintManager";
    // The value of StandardPrintTaskOptions.CustomPageRanges
    private const string CUSTOM_PAGE_RANGES_OPTION = "com.microsoft.JobCustomPageRange";

    /// <summary>
    /// Gets whether the print dialog is available (Windows 10 and later).
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return WinRt.GetActivationFactory<IPrintManagerStatic2>(PRINT_MANAGER_CLASS).IsSupported();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Shows the print dialog, and prints the pages if the user chooses to. Each page is an image that's rotated (if
    /// needed) to match the paper orientation and scaled to fit the paper. The user can choose to print all pages or a
    /// range of pages.
    /// <para/>
    /// This must be called on the owner window's UI thread.
    /// </summary>
    /// <param name="ownerWindow">The handle of the window that the dialog belongs to.</param>
    /// <param name="title">The title of the document, shown in the dialog.</param>
    /// <param name="pageCount">The number of pages.</param>
    /// <param name="getPage">Gets the image for a page, by its zero-based index. This is called on background threads,
    /// one call at a time, and can be called more than once for the same page. The image is disposed once it has been
    /// drawn. Any exception is rethrown by this method.</param>
    /// <returns>True if the document was sent to the printer, or false if the user cancelled.</returns>
    /// <exception cref="PlatformNotSupportedException">The print dialog isn't available (see
    /// <see cref="IsSupported"/>).</exception>
    /// <exception cref="InvalidOperationException">The print job failed.</exception>
    public static async Task<bool> ShowAsync(nint ownerWindow, string title, int pageCount,
        Func<int, PageImage> getPage)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentNullException.ThrowIfNull(getPage);
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("The Windows print dialog requires Windows 10 or later.");
        }

        using var documentSource = new PrintDocumentSource(pageCount, getPage);
        var interop = WinRt.GetActivationFactory<IPrintManagerInterop>(PRINT_MANAGER_CLASS);
        var printManager = WinRt.TakeObject<IPrintManager>(
            interop.GetForWindow(ownerWindow, typeof(IPrintManager).GUID));
        var completion = new TaskCompletionSource<PrintTaskCompletion>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var requestedHandler = WinRt.GetPointer<IPrintTaskRequestedHandler>(new PrintTaskRequestedHandler(args =>
        {
            try
            {
                CreatePrintTask(args, title, documentSource, completion);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                throw;
            }
        }));
        long token = printManager.add_PrintTaskRequested(requestedHandler.Value);
        PrintTaskCompletion result;
        try
        {
            var showOperation = WinRt.TakeObject<IAsyncOperationBoolean>(
                interop.ShowPrintUIForWindowAsync(ownerWindow, typeof(IAsyncOperationBoolean).GUID));
            bool shown;
            try
            {
                shown = await WinRt.AsTask(showOperation);
            }
            catch (Exception) when (completion.Task.IsFaulted)
            {
                // Report the original error from creating the print task rather than the resulting COM error
                await completion.Task;
                throw;
            }
            if (!shown)
            {
                return false;
            }
            result = await completion.Task;
        }
        finally
        {
            printManager.remove_PrintTaskRequested(token);
        }

        if (documentSource.Error != null)
        {
            ExceptionDispatchInfo.Throw(documentSource.Error);
        }
        return result switch
        {
            PrintTaskCompletion.Submitted => true,
            PrintTaskCompletion.Failed => throw new InvalidOperationException("The print job failed."),
            _ => false
        };
    }

    // Called on a background thread
    private static void CreatePrintTask(nint argsPtr, string title, PrintDocumentSource documentSource,
        TaskCompletionSource<PrintTaskCompletion> completion)
    {
        var request = WinRt.GetObject<IPrintTaskRequestedEventArgs>(argsPtr).get_Request();
        using var titleString = new HString(title);
        using var sourceRequestedHandler = WinRt.GetPointer<IPrintTaskSourceRequestedHandler>(
            new PrintTaskSourceRequestedHandler(sourceArgs =>
            {
                using var source = WinRt.GetPointer<IPrintDocumentSource>(documentSource);
                WinRt.GetObject<IPrintTaskSourceRequestedArgs>(sourceArgs).SetSource(source.Value);
            }));
        var printTask = request.CreatePrintTask(titleString.Handle, sourceRequestedHandler.Value);

        var options = printTask.get_Options();
        using var pageRangesOption = new HString(CUSTOM_PAGE_RANGES_OPTION);
        ((IPrintTaskOptionsCoreUIConfiguration) options).get_DisplayedOptions().Append(pageRangesOption.Handle);
        var pageRangeOptions = ((IPrintTaskOptions2) options).get_PageRangeOptions();
        pageRangeOptions.put_AllowAllPages(true);
        pageRangeOptions.put_AllowCustomSetOfPages(true);

        using var completedHandler = WinRt.GetPointer<IPrintTaskCompletedHandler>(
            new PrintTaskCompletedHandler(completedArgs => completion.TrySetResult(
                WinRt.GetObject<IPrintTaskCompletedEventArgs>(completedArgs).get_Completion())));
        printTask.add_Completed(completedHandler.Value);
    }
}
