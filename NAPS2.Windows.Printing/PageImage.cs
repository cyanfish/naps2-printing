namespace NAPS2.Windows.Printing;

/// <summary>
/// An image to print as a page, as 32-bit BGRA pixel data. The alpha channel is ignored.
/// </summary>
/// <param name="width">The width in pixels.</param>
/// <param name="height">The height in pixels.</param>
/// <param name="stride">The number of bytes from the start of one row to the next.</param>
/// <param name="pixels">A pointer to the pixel data, which must stay valid until the image is disposed.</param>
/// <param name="release">Called when the image is disposed, e.g. to free or unlock the pixel data.</param>
public sealed class PageImage(int width, int height, int stride, nint pixels, Action? release = null) : IDisposable
{
    private Action? _release = release;

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width => width;

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height => height;

    /// <summary>
    /// Gets the number of bytes from the start of one row to the next.
    /// </summary>
    public int Stride => stride;

    /// <summary>
    /// Gets a pointer to the pixel data.
    /// </summary>
    public nint Pixels => pixels;

    /// <summary>
    /// Releases the pixel data.
    /// </summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
