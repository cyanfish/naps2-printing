namespace NAPS2.Windows.Printing;

/// <summary>
/// The result of <see cref="WindowsPrintDialog.ShowAsync"/>.
/// </summary>
public sealed class PrintResult
{
    /// <summary>
    /// Gets whether the document was sent to the printer. This is false if the user cancelled.
    /// </summary>
    public bool Printed { get; internal init; }

    /// <summary>
    /// Gets the number of pages sent to the printer, not counting copies.
    /// </summary>
    public int PageCount { get; internal init; }

    /// <summary>
    /// Gets the name of the printer, or null if the document wasn't printed or the printer couldn't be found.
    /// </summary>
    public string? PrinterName { get; internal init; }
}
