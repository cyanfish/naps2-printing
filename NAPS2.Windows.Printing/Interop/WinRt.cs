using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NAPS2.Windows.Printing.Interop;

/// <summary>
/// Helpers for calling WinRT APIs through their COM interfaces, without the Windows SDK projection.
/// </summary>
internal static class WinRt
{
    public static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>
    /// Gets a wrapper for a COM object. The wrapper adds its own reference.
    /// </summary>
    public static T GetObject<T>(nint ptr) =>
        (T) ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);

    /// <summary>
    /// Gets a wrapper for a COM object, releasing the caller's reference.
    /// </summary>
    public static T TakeObject<T>(nint ptr)
    {
        try
        {
            return GetObject<T>(ptr);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>
    /// Gets a COM pointer to the given interface of a managed object.
    /// </summary>
    public static ComPointer GetPointer<T>(object obj)
    {
        var iid = typeof(T).GUID;
        nint unknown = ComWrappers.GetOrCreateComInterfaceForObject(obj, CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint ptr));
            return new ComPointer(ptr);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public static T GetActivationFactory<T>(string className)
    {
        using var classId = new HString(className);
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId.Handle, typeof(T).GUID, out nint factory));
        return TakeObject<T>(factory);
    }

    /// <summary>
    /// Gets the string from a boxed string, releasing the caller's reference.
    /// </summary>
    public static string TakeBoxedString(nint ptr) => HString.Take(TakeObject<IPropertyValue>(ptr).GetString());

    public static Task<bool> AsTask(IAsyncOperationBoolean operation)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = GetPointer<IAsyncOperationCompletedHandlerBoolean>(
            new AsyncOperationCompletedHandler((operationPtr, status) =>
            {
                var completed = GetObject<IAsyncOperationBoolean>(operationPtr);
                switch (status)
                {
                    case AsyncStatus.Completed:
                        result.TrySetResult(completed.GetResults());
                        break;
                    case AsyncStatus.Canceled:
                        result.TrySetCanceled();
                        break;
                    default:
                        result.TrySetException(Marshal.GetExceptionForHR(((IAsyncInfo) completed).get_ErrorCode()) ??
                                               new InvalidOperationException("The operation failed"));
                        break;
                }
            }));
        operation.put_Completed(handler.Value);
        return result.Task;
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);
}

/// <summary>
/// A WinRT string (HSTRING), deleted when disposed.
/// </summary>
internal readonly struct HString : IDisposable
{
    public HString(string value)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint) value.Length, out nint handle));
        Handle = handle;
    }

    public nint Handle { get; }

    public void Dispose() => WindowsDeleteString(Handle);

    /// <summary>
    /// Reads a WinRT string returned by a method, which the caller owns, and deletes it.
    /// </summary>
    public static unsafe string Take(nint hstring)
    {
        try
        {
            char* buffer = WindowsGetStringRawBuffer(hstring, out uint length);
            return new string(buffer, 0, (int) length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern unsafe char* WindowsGetStringRawBuffer(nint hstring, out uint length);
}

/// <summary>
/// A COM interface pointer, released when disposed.
/// </summary>
internal readonly struct ComPointer(nint value) : IDisposable
{
    public nint Value { get; } = value;

    public void Dispose() => Marshal.Release(Value);
}

/// <summary>
/// A managed COM object that can be called from any thread, as WinRT expects of delegates and document sources.
/// </summary>
internal abstract class AgileComObject : ICustomQueryInterface
{
    private static readonly Guid IID_IAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    private static readonly Guid IID_IMarshal = new("00000003-0000-0000-c000-000000000046");

    private readonly object _lock = new();
    private nint _freeThreadedMarshaler;

    public CustomQueryInterfaceResult GetInterface(ref Guid iid, out nint ppv)
    {
        ppv = 0;
        if (iid == IID_IAgileObject)
        {
            ppv = WinRt.ComWrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
            return CustomQueryInterfaceResult.Handled;
        }
        if (iid == IID_IMarshal)
        {
            lock (_lock)
            {
                if (_freeThreadedMarshaler == 0)
                {
                    nint outer = WinRt.ComWrappers.GetOrCreateComInterfaceForObject(this,
                        CreateComInterfaceFlags.None);
                    try
                    {
                        Marshal.ThrowExceptionForHR(CoCreateFreeThreadedMarshaler(outer, out _freeThreadedMarshaler));
                    }
                    finally
                    {
                        Marshal.Release(outer);
                    }
                }
            }
            return Marshal.QueryInterface(_freeThreadedMarshaler, in IID_IMarshal, out ppv) == 0
                ? CustomQueryInterfaceResult.Handled
                : CustomQueryInterfaceResult.Failed;
        }
        return CustomQueryInterfaceResult.NotHandled;
    }

    ~AgileComObject()
    {
        if (_freeThreadedMarshaler != 0)
        {
            Marshal.Release(_freeThreadedMarshaler);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateFreeThreadedMarshaler(nint punkOuter, out nint ppunkMarshal);
}

[GeneratedComClass]
internal partial class PrintTaskRequestedHandler(Action<nint> invoke) : AgileComObject, IPrintTaskRequestedHandler
{
    public void Invoke(nint sender, nint args) => invoke(args);
}

[GeneratedComClass]
internal partial class PrintTaskSourceRequestedHandler(Action<nint> invoke)
    : AgileComObject, IPrintTaskSourceRequestedHandler
{
    public void Invoke(nint args) => invoke(args);
}

[GeneratedComClass]
internal partial class PrintTaskCompletedHandler(Action<nint> invoke) : AgileComObject, IPrintTaskCompletedHandler
{
    public void Invoke(nint sender, nint args) => invoke(args);
}

[GeneratedComClass]
internal partial class AsyncOperationCompletedHandler(Action<nint, AsyncStatus> invoke)
    : AgileComObject, IAsyncOperationCompletedHandlerBoolean
{
    public void Invoke(nint asyncInfo, AsyncStatus status) => invoke(asyncInfo, status);
}

[GeneratedComClass]
internal partial class PackageStatusHandler(Action<PrintDocumentPackageStatus> invoke)
    : AgileComObject, IPrintDocumentPackageStatusEvent
{
    private const int E_NOTIMPL = unchecked((int) 0x80004001);

    public int GetTypeInfoCount(out uint count)
    {
        count = 0;
        return 0;
    }

    public int GetTypeInfo(uint index, uint localeId, out nint typeInfo)
    {
        typeInfo = 0;
        return E_NOTIMPL;
    }

    public int GetIDsOfNames(in Guid iid, nint names, uint nameCount, uint localeId, nint dispIds) => E_NOTIMPL;

    public int Invoke(int dispId, in Guid iid, uint localeId, ushort flags, nint parameters, nint result,
        nint exceptionInfo, nint argumentError) => E_NOTIMPL;

    public void PackageStatusUpdated(in PrintDocumentPackageStatus packageStatus) => invoke(packageStatus);
}

[GeneratedComClass]
internal partial class PrintTaskOptionChangedHandler(Action<nint> invoke)
    : AgileComObject, IPrintTaskOptionChangedHandler
{
    public void Invoke(nint sender, nint args) => invoke(args);
}
