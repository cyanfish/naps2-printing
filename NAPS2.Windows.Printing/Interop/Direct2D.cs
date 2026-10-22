using System.Numerics;
using System.Runtime.InteropServices;

namespace NAPS2.Windows.Printing.Interop;

// Minimal Direct3D 11 and Direct2D bindings for drawing print pages. COM methods are called by their vtable slot, which
// (like the constants and struct layouts) is from the Windows SDK headers (d3d11.h, d2d1.h, d2d1_1.h, dxgi.h).

/// <summary>
/// A reference to a native COM object, released when disposed.
/// </summary>
internal unsafe class NativeComObject(nint ptr) : IDisposable
{
    public nint Ptr { get; private set; } = ptr;

    protected void** Vtbl => *(void***) Ptr;

    public nint QueryInterface(Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(Ptr, in iid, out nint result));
        return result;
    }

    public void Dispose()
    {
        if (Ptr != 0)
        {
            Marshal.Release(Ptr);
            Ptr = 0;
        }
    }
}

internal unsafe class D3D11Device(nint ptr) : NativeComObject(ptr)
{
    private const int DRIVER_TYPE_HARDWARE = 1;
    private const int DRIVER_TYPE_WARP = 5;
    private const uint CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint SDK_VERSION = 7;
    private const uint FORMAT_B8G8R8A8_UNORM = 87;
    private const uint BIND_SHADER_RESOURCE = 0x8;
    private const uint BIND_RENDER_TARGET = 0x20;

    public static D3D11Device Create()
    {
        // Fall back to the software rasterizer if there's no usable GPU
        return TryCreate(DRIVER_TYPE_HARDWARE) ?? TryCreate(DRIVER_TYPE_WARP) ??
               throw new InvalidOperationException("Could not create a Direct3D device");
    }

    private static D3D11Device? TryCreate(int driverType)
    {
        int* featureLevels = stackalloc int[] { 0xb100, 0xb000 };
        int hr = D3D11CreateDevice(0, driverType, 0, CREATE_DEVICE_BGRA_SUPPORT, featureLevels, 2, SDK_VERSION,
            out nint device, out _, out nint context);
        if (context != 0) Marshal.Release(context);
        return hr >= 0 ? new D3D11Device(device) : null;
    }

    /// <summary>
    /// Creates a BGRA texture that Direct2D can draw to.
    /// </summary>
    public NativeComObject CreateRenderTexture(uint width, uint height)
    {
        var desc = new Texture2DDesc
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            BindFlags = BIND_RENDER_TARGET | BIND_SHADER_RESOURCE
        };
        nint texture;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Texture2DDesc*, void*, nint*, int>) Vtbl[5])(
            Ptr, &desc, null, &texture));
        return new NativeComObject(texture);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        int* featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel,
        out nint immediateContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }
}

internal unsafe class D2DDevice(nint ptr) : NativeComObject(ptr)
{
    private const int FACTORY_TYPE_MULTI_THREADED = 1;
    private const uint CLSCTX_INPROC_SERVER = 1;
    private static readonly Guid IID_ID2D1Factory1 = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid CLSID_WICImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IID_IWICImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");

    public static D2DDevice Create(D3D11Device d3dDevice)
    {
        Marshal.ThrowExceptionForHR(D2D1CreateFactory(FACTORY_TYPE_MULTI_THREADED, IID_ID2D1Factory1, 0,
            out nint factoryPtr));
        // The device keeps its own reference to the factory
        using var factory = new D2DFactory(factoryPtr);
        using var dxgiDevice = new NativeComObject(d3dDevice.QueryInterface(IID_IDXGIDevice));
        return new D2DDevice(factory.CreateDevice(dxgiDevice));
    }

    public D2DDeviceContext CreateDeviceContext()
    {
        nint context;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>) Vtbl[4])(
            Ptr, 0, &context));
        return new D2DDeviceContext(context);
    }

    public D2DPrintControl CreatePrintControl(nint documentTarget)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(CLSID_WICImagingFactory, 0, CLSCTX_INPROC_SERVER,
            IID_IWICImagingFactory, out nint wicFactoryPtr));
        using var wicFactory = new NativeComObject(wicFactoryPtr);
        nint printControl;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint, void*, nint*, int>) Vtbl[5])(
            Ptr, wicFactory.Ptr, documentTarget, null, &printControl));
        return new D2DPrintControl(printControl);
    }

    [DllImport("d2d1.dll")]
    private static extern int D2D1CreateFactory(int factoryType, in Guid riid, nint factoryOptions,
        out nint factory);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid,
        out nint obj);

    private class D2DFactory(nint ptr) : NativeComObject(ptr)
    {
        public nint CreateDevice(NativeComObject dxgiDevice)
        {
            nint device;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>) Vtbl[17])(
                Ptr, dxgiDevice.Ptr, &device));
            return device;
        }
    }
}

internal unsafe class D2DDeviceContext(nint ptr) : NativeComObject(ptr)
{
    private const uint FORMAT_B8G8R8A8_UNORM = 87;
    private const uint ALPHA_MODE_PREMULTIPLIED = 1;
    private const uint ALPHA_MODE_IGNORE = 3;
    private const uint BITMAP_OPTIONS_TARGET = 0x1;
    private const uint BITMAP_OPTIONS_CANNOT_DRAW = 0x2;
    private const int INTERPOLATION_MODE_HIGH_QUALITY_CUBIC = 5;

    /// <summary>
    /// Creates a bitmap to draw to from a DXGI surface.
    /// </summary>
    public NativeComObject CreateTargetBitmap(nint dxgiSurface, float dpi)
    {
        var props = new BitmapProperties1
        {
            Format = FORMAT_B8G8R8A8_UNORM,
            AlphaMode = ALPHA_MODE_PREMULTIPLIED,
            DpiX = dpi,
            DpiY = dpi,
            Options = BITMAP_OPTIONS_TARGET | BITMAP_OPTIONS_CANNOT_DRAW
        };
        nint bitmap;
        Marshal.ThrowExceptionForHR(
            ((delegate* unmanaged[Stdcall]<nint, nint, BitmapProperties1*, nint*, int>) Vtbl[62])(
                Ptr, dxgiSurface, &props, &bitmap));
        return new NativeComObject(bitmap);
    }

    /// <summary>
    /// Creates a bitmap from opaque 32-bit BGRA pixel data.
    /// </summary>
    public NativeComObject CreateBitmap(int width, int height, nint data, int stride)
    {
        var props = new BitmapProperties1
        {
            Format = FORMAT_B8G8R8A8_UNORM,
            AlphaMode = ALPHA_MODE_IGNORE
        };
        nint bitmap;
        Marshal.ThrowExceptionForHR(
            ((delegate* unmanaged[Stdcall]<nint, SizeU, nint, uint, BitmapProperties1*, nint*, int>) Vtbl[57])(
                Ptr, new SizeU((uint) width, (uint) height), data, (uint) stride, &props, &bitmap));
        return new NativeComObject(bitmap);
    }

    public D2DCommandList CreateCommandList()
    {
        nint commandList;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>) Vtbl[67])(Ptr, &commandList));
        return new D2DCommandList(commandList);
    }

    /// <summary>
    /// Sets the image (a bitmap or command list) to draw to.
    /// </summary>
    public void SetTarget(NativeComObject? image) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>) Vtbl[74])(Ptr, image?.Ptr ?? 0);

    public void SetDpi(float dpiX, float dpiY) =>
        ((delegate* unmanaged[Stdcall]<nint, float, float, void>) Vtbl[51])(Ptr, dpiX, dpiY);

    public void SetTransform(Matrix3x2 transform) =>
        ((delegate* unmanaged[Stdcall]<nint, Matrix3x2*, void>) Vtbl[30])(Ptr, &transform);

    public void BeginDraw() => ((delegate* unmanaged[Stdcall]<nint, void>) Vtbl[48])(Ptr);

    public void EndDraw() =>
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>) Vtbl[49])(
            Ptr, null, null));

    public void Clear(float r, float g, float b)
    {
        var color = new Vector4(r, g, b, 1);
        ((delegate* unmanaged[Stdcall]<nint, Vector4*, void>) Vtbl[47])(Ptr, &color);
    }

    public void DrawBitmap(NativeComObject bitmap, float left, float top, float right, float bottom)
    {
        var rect = new Vector4(left, top, right, bottom);
        ((delegate* unmanaged[Stdcall]<nint, nint, Vector4*, float, int, Vector4*, void*, void>) Vtbl[85])(
            Ptr, bitmap.Ptr, &rect, 1, INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, null, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapProperties1
    {
        public uint Format;
        public uint AlphaMode;
        public float DpiX;
        public float DpiY;
        public uint Options;
        public nint ColorContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SizeU(uint width, uint height)
    {
        public readonly uint Width = width;
        public readonly uint Height = height;
    }
}

internal unsafe class D2DCommandList(nint ptr) : NativeComObject(ptr)
{
    public void Close() =>
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>) Vtbl[5])(Ptr));
}

internal unsafe class D2DPrintControl(nint ptr) : NativeComObject(ptr)
{
    public void AddPage(D2DCommandList commandList, float width, float height) =>
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Vector2, nint, ulong*, ulong*, int>)
            Vtbl[3])(Ptr, commandList.Ptr, new Vector2(width, height), 0, null, null));

    public void Close() =>
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>) Vtbl[4])(Ptr));
}
