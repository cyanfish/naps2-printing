using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using NAPS2.Windows.Printing.Interop;

namespace NAPS2.Windows.Printing;

/// <summary>
/// Prints images with the Windows print dialog, which shows a preview of the pages.
/// </summary>
public static class WindowsPrintDialog
{
    private const string PRINT_MANAGER_CLASS = "Windows.Graphics.Printing.PrintManager";
    private const string PAGE_RANGE_CLASS = "Windows.Graphics.Printing.PrintPageRange";
    private const string OPTION_DETAILS_CLASS = "Windows.Graphics.Printing.OptionDetails.PrintTaskOptionDetails";
    // The value of StandardPrintTaskOptions.CustomPageRanges
    private const string CUSTOM_PAGE_RANGES_OPTION = "com.microsoft.JobCustomPageRange";
    private const string COLOR_MODE_OPTION = "PageOutputColor";

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
    /// needed) to match the paper orientation and scaled to fit the paper. The user can choose to print all pages, the
    /// current page or a range of pages.
    /// <para/>
    /// This must be called on the owner window's UI thread.
    /// </summary>
    /// <param name="ownerWindow">The handle of the window that the dialog belongs to.</param>
    /// <param name="title">The title of the document, shown in the dialog.</param>
    /// <param name="pageCount">The number of pages.</param>
    /// <param name="getPage">Gets the image for a page, by its zero-based index. This is called on background threads,
    /// one call at a time, and can be called more than once for the same page. The image is disposed once it has been
    /// drawn. Any exception is rethrown by this method.</param>
    /// <param name="options">Options for the dialog, or null for the defaults.</param>
    /// <returns>True if the document was sent to the printer, or false if the user cancelled.</returns>
    /// <exception cref="PlatformNotSupportedException">The print dialog isn't available (see
    /// <see cref="IsSupported"/>).</exception>
    /// <exception cref="InvalidOperationException">The print job failed.</exception>
    public static async Task<bool> ShowAsync(nint ownerWindow, string title, int pageCount,
        Func<int, PageImage> getPage, PrintDialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentNullException.ThrowIfNull(getPage);
        options ??= new PrintDialogOptions();
        if (options.StartPage < 0 || options.StartPage >= Math.Max(pageCount, 1))
        {
            throw new ArgumentException("The start page is out of range.", nameof(options));
        }
        if (options.InitialPages?.Any(page => page < 0 || page >= pageCount) == true)
        {
            throw new ArgumentException("The initial pages are out of range.", nameof(options));
        }
        if (options.Orientation is { } orientation && !Enum.IsDefined(orientation))
        {
            throw new ArgumentException("The orientation isn't valid.", nameof(options));
        }
        if (options.DisplayedOptions?.Any(option => !Enum.IsDefined(option)) == true)
        {
            throw new ArgumentException("The displayed options aren't valid.", nameof(options));
        }
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("The Windows print dialog requires Windows 10 or later.");
        }

        using var documentSource = new PrintDocumentSource(pageCount, getPage, options);
        var interop = WinRt.GetActivationFactory<IPrintManagerInterop>(PRINT_MANAGER_CLASS);
        var printManager = WinRt.TakeObject<IPrintManager>(
            interop.GetForWindow(ownerWindow, typeof(IPrintManager).GUID));
        var completion = new TaskCompletionSource<PrintTaskCompletion>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // The option details raise the page range events, so a reference is kept while the dialog is open
        IPrintTaskOptionDetails? optionDetails = null;

        using var requestedHandler = WinRt.GetPointer<IPrintTaskRequestedHandler>(new PrintTaskRequestedHandler(args =>
        {
            try
            {
                optionDetails = CreatePrintTask(args, title, options, documentSource, completion);
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
            GC.KeepAlive(optionDetails);
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
    private static IPrintTaskOptionDetails CreatePrintTask(nint argsPtr, string title, PrintDialogOptions dialogOptions,
        PrintDocumentSource documentSource, TaskCompletionSource<PrintTaskCompletion> completion)
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
        SetDisplayedOptions(((IPrintTaskOptionsCoreUIConfiguration) options).get_DisplayedOptions(),
            dialogOptions.DisplayedOptions);
        if (dialogOptions.Orientation is { } orientation)
        {
            ((IPrintTaskOptionsCoreProperties) options).put_Orientation(orientation == PrintOrientation.Landscape
                ? PrintOrientationValue.Landscape
                : PrintOrientationValue.Portrait);
        }
        var pageRangeOptions = ((IPrintTaskOptions2) options).get_PageRangeOptions();
        pageRangeOptions.put_AllowAllPages(true);
        pageRangeOptions.put_AllowCurrentPage(dialogOptions.AllowCurrentPage);
        pageRangeOptions.put_AllowCustomSetOfPages(true);
        if (dialogOptions.InitialPages != null)
        {
            // The dialog shows these as a custom range
            AddPageRanges(((IPrintTaskOptions2) options).get_CustomPageRanges(), dialogOptions.InitialPages);
        }

        // The dialog doesn't update the preview when the page range or color mode changes
        var optionDetails = WinRt.GetActivationFactory<IPrintTaskOptionDetailsStatic>(OPTION_DETAILS_CLASS)
            .GetFromPrintTaskOptions(options);
        using var optionChangedHandler = WinRt.GetPointer<IPrintTaskOptionChangedHandler>(
            new PrintTaskOptionChangedHandler(changedArgs =>
            {
                var optionId = WinRt.GetObject<IPrintTaskOptionChangedEventArgs>(changedArgs).get_OptionId();
                switch (WinRt.TakeBoxedString(optionId))
                {
                    case CUSTOM_PAGE_RANGES_OPTION:
                        documentSource.RefreshPreview();
                        break;
                    case COLOR_MODE_OPTION:
                        documentSource.RedrawPreview();
                        break;
                }
            }));
        optionDetails.add_OptionChanged(optionChangedHandler.Value);

        using var completedHandler = WinRt.GetPointer<IPrintTaskCompletedHandler>(
            new PrintTaskCompletedHandler(completedArgs => completion.TrySetResult(
                WinRt.GetObject<IPrintTaskCompletedEventArgs>(completedArgs).get_Completion())));
        printTask.add_Completed(completedHandler.Value);
        return optionDetails;
    }

    private static void SetDisplayedOptions(IVectorString displayed, IReadOnlyList<PrintOption>? displayedOptions)
    {
        var optionIds = new List<string>();
        if (displayedOptions != null)
        {
            displayed.Clear();
            optionIds.AddRange(displayedOptions.Distinct().Select(GetOptionId));
        }
        if (displayedOptions?.Contains(PrintOption.PageRange) != true)
        {
            optionIds.Add(CUSTOM_PAGE_RANGES_OPTION);
        }
        foreach (var optionId in optionIds)
        {
            using var optionIdString = new HString(optionId);
            displayed.Append(optionIdString.Handle);
        }
    }

    // The values of the StandardPrintTaskOptions properties. The dialog fails to open if it gets an unknown option.
    private static string GetOptionId(PrintOption option) => option switch
    {
        PrintOption.Copies => "JobCopiesAllDocuments",
        PrintOption.Orientation => "PageOrientation",
        PrintOption.ColorMode => COLOR_MODE_OPTION,
        PrintOption.PageRange => CUSTOM_PAGE_RANGES_OPTION,
        PrintOption.PaperSize => "PageMediaSize",
        PrintOption.PaperType => "PageMediaType",
        PrintOption.Duplex => "DocumentDuplex",
        PrintOption.Collation => "DocumentCollate",
        PrintOption.Quality => "PageOutputQuality",
        PrintOption.Borderless => "PageBorderless",
        PrintOption.Staple => "DocumentStaple",
        PrintOption.HolePunch => "DocumentHolePunch",
        PrintOption.Binding => "DocumentBinding",
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    private static void AddPageRanges(IVectorPrintPageRange ranges, IReadOnlyCollection<int> pageIndices)
    {
        var factory = WinRt.GetActivationFactory<IPrintPageRangeFactory>(PAGE_RANGE_CLASS);
        var sorted = pageIndices.Distinct().Order().ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int first = sorted[i];
            while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1)
            {
                i++;
            }
            // Page numbers are one-based
            nint range = factory.Create(first + 1, sorted[i] + 1);
            try
            {
                ranges.Append(range);
            }
            finally
            {
                Marshal.Release(range);
            }
        }
    }
}
