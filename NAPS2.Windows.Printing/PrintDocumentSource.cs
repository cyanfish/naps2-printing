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

    private readonly int _pageCount;
    private readonly Func<int, PageImage> _getPage;
    private readonly D3D11Device _d3dDevice;
    private readonly D2DDevice _d2dDevice;
    // The print dialog calls from multiple threads, so calls are serialized with this lock
    private readonly object _lock = new();
    private IPrintPreviewDxgiPackageTarget? _previewTarget;
    private IPrintTaskOptionsCore? _previewOptions;
    private bool _disposed;

    public PrintDocumentSource(int pageCount, Func<int, PageImage> getPage)
    {
        _pageCount = pageCount;
        _getPage = getPage;
        _d3dDevice = D3D11Device.Create();
        _d2dDevice = D2DDevice.Create(_d3dDevice);
    }

    /// <summary>
    /// Gets the first error from providing pages, which the print dialog would otherwise only see as an HRESULT.
    /// </summary>
    public Exception? Error { get; private set; }

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
        _previewTarget!.InvalidatePreview();
        _previewTarget.SetJobPageCount(PageCountType.FinalPageCount, (uint) GetPageIndices(_previewOptions).Count);
    });

    public void MakePage(uint desiredJobPage, float width, float height) => Run(() =>
    {
        var pageIndices = GetPageIndices(_previewOptions!);
        if (desiredJobPage == JOB_PAGE_APPLICATION_DEFINED)
        {
            desiredJobPage = 1;
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
        // The preview size is in DIPs, so the page is scaled to fit it
        DrawPage(context, pageIndices[(int) desiredJobPage - 1], page, Matrix3x2.CreateScale(width / page.PageWidth));
        context.EndDraw();
        context.SetTarget(null);

        _previewTarget!.DrawPage(desiredJobPage, surface.Ptr, PREVIEW_DPI, PREVIEW_DPI);
    });

    public void MakeDocument(nint printTaskOptions, nint docPackageTarget) => Run(() =>
    {
        var options = WinRt.GetObject<IPrintTaskOptionsCore>(printTaskOptions);
        var pageIndices = GetPageIndices(options);
        using var printControl = _d2dDevice.CreatePrintControl(docPackageTarget);
        using var context = _d2dDevice.CreateDeviceContext();
        for (int i = 0; i < pageIndices.Count; i++)
        {
            var page = options.GetPageDescription((uint) i + 1);
            using var commandList = context.CreateCommandList();
            context.SetTarget(commandList);
            context.BeginDraw();
            DrawPage(context, pageIndices[i], page, Matrix3x2.Identity);
            context.EndDraw();
            context.SetTarget(null);
            commandList.Close();
            printControl.AddPage(commandList, page.PageWidth, page.PageHeight);
        }
        printControl.Close();
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

    private void DrawPage(D2DDeviceContext context, int pageIndex, PrintPageDescription page, Matrix3x2 transform)
    {
        using var image = _getPage(pageIndex);
        using var bitmap = context.CreateBitmap(image.Width, image.Height, image.Pixels, image.Stride);

        // Rotate the image if needed to match the page orientation, then scale it to fit in the top left of the page
        bool rotate = image.Width > image.Height != page.PageWidth > page.PageHeight;
        float rotatedWidth = rotate ? image.Height : image.Width;
        float rotatedHeight = rotate ? image.Width : image.Height;
        float scale = Math.Min(page.PageWidth / rotatedWidth, page.PageHeight / rotatedHeight);
        var imageTransform = Matrix3x2.CreateScale(scale);
        if (rotate)
        {
            imageTransform *= Matrix3x2.CreateRotation(MathF.PI / 2) *
                              Matrix3x2.CreateTranslation(rotatedWidth * scale, 0);
        }
        context.SetTransform(imageTransform * transform);
        context.DrawBitmap(bitmap, 0, 0, image.Width, image.Height);
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
