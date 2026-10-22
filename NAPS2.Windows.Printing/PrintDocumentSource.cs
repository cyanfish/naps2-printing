using System.Numerics;
using System.Runtime.InteropServices.Marshalling;
using NAPS2.Windows.Printing.Interop;

namespace NAPS2.Windows.Printing;

/// <summary>
/// Provides the pages to the print dialog, both as preview images and as the printed document. This implements the COM
/// interfaces that WinUI's PrintDocument wraps, drawing the pages with Direct2D.
/// </summary>
[GeneratedComClass]
internal partial class PrintDocumentSource : AgileComObject, IPrintDocumentSource, IPrintDocumentPageSource,
    IPrintPreviewPageCollection, IDisposable
{
    private const uint JOB_PAGE_APPLICATION_DEFINED = uint.MaxValue;
    private const float PREVIEW_DPI = 96;
    private static readonly Guid PreviewPackageTargetDxgi = new("1a6dd0ad-1e2a-4e99-a5ba-91f17818290e");
    private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
    // The dialog reports the page range again within a few milliseconds of the preview moving
    private static readonly TimeSpan CurrentPageCheckTimeout = TimeSpan.FromMilliseconds(300);

    private readonly int _pageCount;
    private readonly Func<int, PageImage> _getPage;
    private readonly int _startPage;
    private readonly bool _rotatePages;
    private readonly bool _fitToPrintableArea;
    private readonly D3D11Device _d3dDevice;
    private readonly D2DDevice _d2dDevice;
    // The print dialog calls from multiple threads, so calls are serialized with this lock
    private readonly object _lock = new();
    private IPrintPreviewDxgiPackageTarget? _previewTarget;
    private IPrintTaskOptionsCore? _previewOptions;
    // The pages in the preview by job page number, the job page it starts on and the job page it shows
    private List<int> _previewPages = [];
    private uint _previewStartPage = 1;
    private uint _shownJobPage;
    // Whether the page range changed since the preview was paginated, so the preview goes back to the start
    private bool _pageRangeChanged;
    // A page assumed to be the dialog's "current page" until the dialog reports it
    private int? _currentPageCheck;
    private int _currentPageCheckPageNumber;
    private bool _disposed;

    public PrintDocumentSource(int pageCount, Func<int, PageImage> getPage, PrintDialogOptions options)
    {
        _pageCount = pageCount;
        _getPage = getPage;
        _startPage = options.StartPage;
        _rotatePages = options.RotatePages;
        _fitToPrintableArea = options.FitToPrintableArea;
        _d3dDevice = D3D11Device.Create();
        _d2dDevice = D2DDevice.Create(_d3dDevice);
    }

    /// <summary>
    /// Gets the first error from providing pages, which the print dialog would otherwise only see as an HRESULT.
    /// </summary>
    public Exception? Error { get; private set; }

    /// <summary>
    /// Gets the number of pages in the printed document, or 0 if it hasn't been printed.
    /// </summary>
    public int PrintedPageCount { get; private set; }

    /// <summary>
    /// Gets the name of the printer that the document was printed to, if it was found.
    /// </summary>
    public string? PrinterName { get; private set; }

    /// <summary>
    /// Updates the preview after the page range changes.
    /// </summary>
    public void RefreshPreview() => Run(() =>
    {
        if (_previewOptions != null)
        {
            _pageRangeChanged = true;
            UpdatePreview(_previewOptions, JOB_PAGE_APPLICATION_DEFINED);
        }
    });

    /// <summary>
    /// Redraws the preview pages after an option that affects how they look changes.
    /// </summary>
    public void RedrawPreview() => Run(() =>
    {
        if (_previewTarget != null && _shownJobPage > 0)
        {
            _previewStartPage = _shownJobPage;
            _previewTarget.InvalidatePreview();
            _previewTarget.SetJobPageCount(PageCountType.FinalPageCount, (uint) _previewPages.Count);
        }
    });

    public int GetIids(out uint iidCount, out nint iids)
    {
        iidCount = 0;
        iids = 0;
        return 0;
    }

    public int GetRuntimeClassName(out nint className)
    {
        className = 0;
        return 0;
    }

    public int GetTrustLevel(out int trustLevel)
    {
        trustLevel = 0;
        return 0;
    }

    public void GetPreviewPageCollection(nint docPackageTarget, out nint docPageCollection)
    {
        Run(() =>
        {
            var packageTarget = WinRt.GetObject<IPrintDocumentPackageTarget>(docPackageTarget);
            _previewTarget = WinRt.TakeObject<IPrintPreviewDxgiPackageTarget>(packageTarget.GetPackageTarget(
                PreviewPackageTargetDxgi, typeof(IPrintPreviewDxgiPackageTarget).GUID));
        });
        // The caller takes ownership of the reference
        docPageCollection = WinRt.GetPointer<IPrintPreviewPageCollection>(this).Value;
    }

    public void Paginate(uint currentJobPage, nint printTaskOptions) => Run(() =>
    {
        _previewOptions = WinRt.GetObject<IPrintTaskOptionsCore>(printTaskOptions);
        UpdatePreview(_previewOptions, _pageRangeChanged ? JOB_PAGE_APPLICATION_DEFINED : currentJobPage);
        _pageRangeChanged = false;
    });

    public void MakePage(uint desiredJobPage, float width, float height) => Run(() =>
    {
        var pageIndices = _previewPages;
        if (desiredJobPage == JOB_PAGE_APPLICATION_DEFINED)
        {
            // The dialog lets the app choose the page, which becomes its "current page"
            desiredJobPage = _previewStartPage;
        }
        if (desiredJobPage < 1 || desiredJobPage > pageIndices.Count)
        {
            return;
        }
        var page = _previewOptions!.GetPageDescription(desiredJobPage);

        using var texture = _d3dDevice.CreateRenderTexture((uint) Math.Ceiling(width), (uint) Math.Ceiling(height));
        using var surface = new NativeComObject(texture.QueryInterface(IID_IDXGISurface));
        using var context = _d2dDevice.CreateDeviceContext();
        using var target = context.CreateTargetBitmap(surface.Ptr, PREVIEW_DPI);
        context.SetTarget(target);
        context.SetDpi(PREVIEW_DPI, PREVIEW_DPI);
        context.BeginDraw();
        context.Clear(1, 1, 1);
        // The preview size is in DIPs, so the page is scaled to fit it. The printer converts the colors when printing,
        // but the preview needs to show that.
        var colorMode = ((IPrintTaskOptionsCoreProperties) _previewOptions).get_ColorMode();
        DrawPage(context, pageIndices[(int) desiredJobPage - 1], page, Matrix3x2.CreateScale(width / page.PageWidth),
            colorMode is PrintColorMode.Grayscale or PrintColorMode.Monochrome);
        context.EndDraw();
        context.SetTarget(null);

        _previewTarget!.DrawPage(desiredJobPage, surface.Ptr, PREVIEW_DPI, PREVIEW_DPI);
        _shownJobPage = desiredJobPage;
    });

    public void MakeDocument(nint printTaskOptions, nint docPackageTarget) => Run(() =>
    {
        var options = WinRt.GetObject<IPrintTaskOptionsCore>(printTaskOptions);
        var pageIndices = GetPageIndices(options);
        using var jobIdListener = new JobIdListener(docPackageTarget);
        using var printControl = _d2dDevice.CreatePrintControl(docPackageTarget);
        using var context = _d2dDevice.CreateDeviceContext();
        for (int i = 0; i < pageIndices.Count; i++)
        {
            var page = options.GetPageDescription((uint) i + 1);
            using var commandList = context.CreateCommandList();
            context.SetTarget(commandList);
            context.BeginDraw();
            DrawPage(context, pageIndices[i], page, Matrix3x2.Identity, false);
            context.EndDraw();
            context.SetTarget(null);
            commandList.Close();
            printControl.AddPage(commandList, page.PageWidth, page.PageHeight);
        }
        // The job is only in the spooler until the document is closed
        PrinterName = jobIdListener.JobId is { } jobId ? Spooler.FindPrinterForJob(jobId) : null;
        printControl.Close();
        PrintedPageCount = pageIndices.Count;
    });

    private void Run(Action action)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Error ??= ex;
                throw;
            }
        }
    }

    /// <summary>
    /// Sets the pages in the preview to the pages to print. A single page is shown in place in the whole document
    /// instead, as the dialog takes the page shown in the preview as its "current page", and would see page 1. If the
    /// pages are the same as before (e.g. the orientation changed), the preview stays on the current job page.
    /// </summary>
    private void UpdatePreview(IPrintTaskOptionsCore options, uint currentJobPage)
    {
        var pageIndices = GetPageIndices(options);
        int startPage;
        if (pageIndices.Count == 1)
        {
            startPage = GetSinglePage(pageIndices[0]);
            _previewPages = Enumerable.Range(0, _pageCount).ToList();
        }
        else
        {
            _currentPageCheck = null;
            // The given start page only applies until the preview has shown a page
            startPage = currentJobPage >= 1 && currentJobPage <= _previewPages.Count &&
                        pageIndices.SequenceEqual(_previewPages)
                ? _previewPages[(int) currentJobPage - 1]
                : _shownJobPage == 0 && pageIndices.Contains(_startPage)
                    ? _startPage
                    : pageIndices.FirstOrDefault();
            _previewPages = pageIndices;
        }
        _previewStartPage = (uint) Math.Max(_previewPages.IndexOf(startPage), 0) + 1;
        _previewTarget!.InvalidatePreview();
        _previewTarget.SetJobPageCount(PageCountType.FinalPageCount, (uint) _previewPages.Count);
    }

    /// <summary>
    /// Gets the page to show for a page range of one page. When the user chooses "Current page" while the preview only
    /// has some pages, the dialog gives the job page number in the preview, not the page number in the document. That
    /// looks the same as typing the number, so this shows the page that was in the preview, and if the dialog doesn't
    /// then report that page as the current page, goes back to the page with that number.
    /// </summary>
    private int GetSinglePage(int pageIndex)
    {
        int pageNumber = pageIndex + 1;
        if (_currentPageCheck is { } checkedPage)
        {
            if (pageIndex == checkedPage)
            {
                _currentPageCheck = null;
                return pageIndex;
            }
            if (pageNumber == _currentPageCheckPageNumber)
            {
                return checkedPage;
            }
            _currentPageCheck = null;
        }
        if (_previewPages.Count < _pageCount && pageNumber == _shownJobPage && pageNumber <= _previewPages.Count &&
            _previewPages[pageNumber - 1] != pageIndex)
        {
            int previewedPage = _previewPages[pageNumber - 1];
            _currentPageCheck = previewedPage;
            _currentPageCheckPageNumber = pageNumber;
            Task.Delay(CurrentPageCheckTimeout).ContinueWith(_ => EndCurrentPageCheck(previewedPage));
            return previewedPage;
        }
        return pageIndex;
    }

    private void EndCurrentPageCheck(int previewedPage)
    {
        lock (_lock)
        {
            if (_disposed || _currentPageCheck != previewedPage)
            {
                return;
            }
            _currentPageCheck = null;
            try
            {
                UpdatePreview(_previewOptions!, JOB_PAGE_APPLICATION_DEFINED);
            }
            catch (Exception)
            {
                // The dialog may have closed
            }
        }
    }

    /// <summary>
    /// Gets the indices of the pages to print, which is all of them unless the user chose a page range.
    /// </summary>
    private List<int> GetPageIndices(IPrintTaskOptionsCore options)
    {
        var ranges = ((IPrintTaskOptions2) options).get_CustomPageRanges();
        uint rangeCount = ranges.get_Size();
        if (rangeCount == 0)
        {
            return Enumerable.Range(0, _pageCount).ToList();
        }
        var pageIndices = new List<int>();
        for (uint i = 0; i < rangeCount; i++)
        {
            var range = ranges.GetAt(i);
            for (int pageNumber = range.get_FirstPageNumber(); pageNumber <= range.get_LastPageNumber(); pageNumber++)
            {
                if (pageNumber >= 1 && pageNumber <= _pageCount)
                {
                    pageIndices.Add(pageNumber - 1);
                }
            }
        }
        return pageIndices;
    }

    private void DrawPage(D2DDeviceContext context, int pageIndex, PrintPageDescription page, Matrix3x2 transform,
        bool grayscale)
    {
        using var image = _getPage(pageIndex);
        using var bitmap = grayscale
            ? CreateGrayscaleBitmap(context, image)
            : context.CreateBitmap(image.Width, image.Height, image.Pixels, image.Stride);

        bool useImageableArea = _fitToPrintableArea && page.ImageableWidth > 0 && page.ImageableHeight > 0;
        float areaX = useImageableArea ? page.ImageableX : 0;
        float areaY = useImageableArea ? page.ImageableY : 0;
        float areaWidth = useImageableArea ? page.ImageableWidth : page.PageWidth;
        float areaHeight = useImageableArea ? page.ImageableHeight : page.PageHeight;

        // Rotate the image if needed to match the page orientation, then scale it to fit in the top left of the area
        bool rotate = _rotatePages && image.Width > image.Height != areaWidth > areaHeight;
        float rotatedWidth = rotate ? image.Height : image.Width;
        float rotatedHeight = rotate ? image.Width : image.Height;
        float scale = Math.Min(areaWidth / rotatedWidth, areaHeight / rotatedHeight);
        var imageTransform = Matrix3x2.CreateScale(scale);
        if (rotate)
        {
            imageTransform *= Matrix3x2.CreateRotation(MathF.PI / 2) *
                              Matrix3x2.CreateTranslation(rotatedWidth * scale, 0);
        }
        imageTransform *= Matrix3x2.CreateTranslation(areaX, areaY);
        context.SetTransform(imageTransform * transform);
        context.DrawBitmap(bitmap, 0, 0, image.Width, image.Height);
    }

    private static unsafe NativeComObject CreateGrayscaleBitmap(D2DDeviceContext context, PageImage image)
    {
        int stride = image.Width * 4;
        var pixels = new byte[stride * image.Height];
        for (int y = 0; y < image.Height; y++)
        {
            var source = new ReadOnlySpan<byte>((byte*) image.Pixels + (long) y * image.Stride, stride);
            var target = pixels.AsSpan(y * stride, stride);
            for (int x = 0; x < stride; x += 4)
            {
                // BGRA, with the luma weights from ITU-R BT.601
                byte gray = (byte) ((source[x] * 29 + source[x + 1] * 150 + source[x + 2] * 77) >> 8);
                target[x] = target[x + 1] = target[x + 2] = gray;
                target[x + 3] = source[x + 3];
            }
        }
        fixed (byte* ptr = pixels)
        {
            return context.CreateBitmap(image.Width, image.Height, (nint) ptr, stride);
        }
    }

    /// <summary>
    /// Gets the ID of the print job from the document target's status events.
    /// </summary>
    private sealed class JobIdListener : IDisposable
    {
        private readonly IConnectionPoint? _connectionPoint;
        private readonly uint _cookie;

        public JobIdListener(nint docPackageTarget)
        {
            try
            {
                var container = WinRt.GetObject<IConnectionPointContainer>(docPackageTarget);
                _connectionPoint = container.FindConnectionPoint(typeof(IPrintDocumentPackageStatusEvent).GUID);
                using var handler = WinRt.GetPointer<IPrintDocumentPackageStatusEvent>(new PackageStatusHandler(
                    status =>
                    {
                        if (status.JobId != 0)
                        {
                            JobId = status.JobId;
                        }
                    }));
                _cookie = _connectionPoint.Advise(handler.Value);
            }
            catch (Exception)
            {
                // The printer name is optional, so failing to get the job ID doesn't stop printing
                _connectionPoint = null;
            }
        }

        public uint? JobId { get; private set; }

        public void Dispose()
        {
            try
            {
                _connectionPoint?.Unadvise(_cookie);
            }
            catch (Exception)
            {
                // The print job may have ended
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _d2dDevice.Dispose();
            _d3dDevice.Dispose();
        }
    }
}
