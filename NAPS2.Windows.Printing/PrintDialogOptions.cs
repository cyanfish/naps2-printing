namespace NAPS2.Windows.Printing;

/// <summary>
/// Options for the print dialog shown by <see cref="WindowsPrintDialog.ShowAsync"/>.
/// </summary>
public sealed class PrintDialogOptions
{
    /// <summary>
    /// Gets or sets the zero-based index of the page that the preview starts on, which is also the page printed if the
    /// user chooses "Current page" without moving the preview. If the page isn't in <see cref="InitialPages"/>, the
    /// preview starts on the first page to print.
    /// </summary>
    public int StartPage { get; init; }

    /// <summary>
    /// Gets or sets the zero-based indices of the pages to print by default, which the dialog shows as a custom page
    /// range. If null or empty, the dialog defaults to all pages.
    /// </summary>
    public IReadOnlyCollection<int>? InitialPages { get; init; }

    /// <summary>
    /// Gets or sets whether the user can choose to print the current page in the preview. The default is true.
    /// </summary>
    public bool AllowCurrentPage { get; init; } = true;

    /// <summary>
    /// Gets or sets the paper orientation that the dialog starts with, or null for the printer's default.
    /// </summary>
    public PrintOrientation? Orientation { get; init; }

    /// <summary>
    /// Gets or sets whether pages are rotated to match the paper orientation, so that e.g. a landscape image fills
    /// portrait paper. The default is true.
    /// </summary>
    public bool RotatePages { get; init; } = true;

    /// <summary>
    /// Gets or sets whether pages are scaled to fit the printable area of the paper, inside the printer's margins,
    /// instead of the whole paper. If false, the edges of an image that fills the paper may not be printed. The
    /// default is false.
    /// </summary>
    public bool FitToPrintableArea { get; init; }

    /// <summary>
    /// Gets or sets the options shown in the main part of the dialog, in order, with the rest shown under "More
    /// settings". The page range is always shown, after the other options unless it's in the list. Options that the
    /// printer doesn't support aren't shown. If null, the dialog shows its default options.
    /// </summary>
    public IReadOnlyList<PrintOption>? DisplayedOptions { get; init; }
}
