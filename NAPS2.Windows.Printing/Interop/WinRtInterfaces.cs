using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NAPS2.Windows.Printing.Interop;

// WinRT printing interfaces (Windows.Graphics.Printing), with the layouts and IIDs from the Windows SDK headers
// (windows.graphics.printing.h, printmanagerinterop.h and asyncinfo.h). Interfaces only declare the methods up to the
// last one used, as the vtable order is what matters.

[GeneratedComInterface]
[Guid("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90")]
internal partial interface IInspectable
{
    [PreserveSig]
    int GetIids(out uint iidCount, out nint iids);

    [PreserveSig]
    int GetRuntimeClassName(out nint className);

    [PreserveSig]
    int GetTrustLevel(out int trustLevel);
}

[GeneratedComInterface]
[Guid("c5435a42-8d43-4e7b-a68a-ef311e392087")]
internal partial interface IPrintManagerInterop : IInspectable
{
    nint GetForWindow(nint appWindow, in Guid riid);

    nint ShowPrintUIForWindowAsync(nint appWindow, in Guid riid);
}

[GeneratedComInterface]
[Guid("35a99955-e6ab-4139-9abd-b86a729b3598")]
internal partial interface IPrintManagerStatic2 : IInspectable
{
    [return: MarshalAs(UnmanagedType.U1)]
    bool IsSupported();
}

[GeneratedComInterface]
[Guid("ff2a9694-8c99-44fd-ae4a-19d9aa9a0f0a")]
internal partial interface IPrintManager : IInspectable
{
    long add_PrintTaskRequested(nint eventHandler);

    void remove_PrintTaskRequested(long eventCookie);
}

/// <summary>
/// TypedEventHandler&lt;PrintManager, PrintTaskRequestedEventArgs&gt;
/// </summary>
[GeneratedComInterface]
[Guid("8a8cb877-70c5-54ce-8b42-d790e2914859")]
internal partial interface IPrintTaskRequestedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("d0aff924-a31b-454c-a7b6-5d0cc522fc16")]
internal partial interface IPrintTaskRequestedEventArgs : IInspectable
{
    IPrintTaskRequest get_Request();
}

[GeneratedComInterface]
[Guid("6ff61e2e-2722-4240-a67c-f364849a17f3")]
internal partial interface IPrintTaskRequest : IInspectable
{
    long get_Deadline();

    IPrintTask CreatePrintTask(nint title, nint handler);
}

[GeneratedComInterface]
[Guid("6c109fa8-5cb6-4b3a-8663-f39cb02dc9b4")]
internal partial interface IPrintTaskSourceRequestedHandler
{
    void Invoke(nint args);
}

[GeneratedComInterface]
[Guid("f9f067be-f456-41f0-9c98-5ce73e851410")]
internal partial interface IPrintTaskSourceRequestedArgs : IInspectable
{
    long get_Deadline();

    void SetSource(nint source);
}

/// <summary>
/// Windows.Graphics.Printing.IPrintDocumentSource, which has no methods of its own.
/// </summary>
[GeneratedComInterface]
[Guid("dedc0c30-f1eb-47df-aae6-ed5427511f01")]
internal partial interface IPrintDocumentSource : IInspectable;

[GeneratedComInterface]
[Guid("61d80247-6cf6-4fad-84e2-a5e82e2d4ceb")]
internal partial interface IPrintTask : IInspectable
{
    nint get_Properties();

    nint get_Source();

    IPrintTaskOptionsCore get_Options();

    long add_Previewing(nint eventHandler);

    void remove_Previewing(long eventCookie);

    long add_Submitting(nint eventHandler);

    void remove_Submitting(long eventCookie);

    long add_Progressing(nint eventHandler);

    void remove_Progressing(long eventCookie);

    long add_Completed(nint eventHandler);
}

/// <summary>
/// TypedEventHandler&lt;PrintTask, PrintTaskCompletedEventArgs&gt;
/// </summary>
[GeneratedComInterface]
[Guid("b0b02549-b9ad-5226-898a-7b563b46640c")]
internal partial interface IPrintTaskCompletedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("5bcd34af-24e9-4c10-8d07-14c346ba3fce")]
internal partial interface IPrintTaskCompletedEventArgs : IInspectable
{
    PrintTaskCompletion get_Completion();
}

internal enum PrintTaskCompletion
{
    Abandoned = 0,
    Canceled = 1,
    Failed = 2,
    Submitted = 3
}

[GeneratedComInterface]
[Guid("1bdbb474-4ed1-41eb-be3c-72d18ed67337")]
internal partial interface IPrintTaskOptionsCore : IInspectable
{
    PrintPageDescription GetPageDescription(uint jobPageNumber);
}

/// <summary>
/// The page and printable area sizes are in DIPs (1/96 inch).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PrintPageDescription
{
    public float PageWidth;
    public float PageHeight;
    public float ImageableX;
    public float ImageableY;
    public float ImageableWidth;
    public float ImageableHeight;
    public uint DpiX;
    public uint DpiY;
}

[GeneratedComInterface]
[Guid("62e69e23-9a1e-4336-b74f-3cc7f4cff709")]
internal partial interface IPrintTaskOptionsCoreUIConfiguration : IInspectable
{
    IVectorString get_DisplayedOptions();
}

[GeneratedComInterface]
[Guid("eb9b1606-9a36-4b59-8617-b217849262e1")]
internal partial interface IPrintTaskOptions2 : IInspectable
{
    IPrintPageRangeOptions get_PageRangeOptions();

    IVectorPrintPageRange get_CustomPageRanges();
}

[GeneratedComInterface]
[Guid("ce6db728-1357-46b2-a923-79f995f448fc")]
internal partial interface IPrintPageRangeOptions : IInspectable
{
    void put_AllowAllPages([MarshalAs(UnmanagedType.U1)] bool value);

    [return: MarshalAs(UnmanagedType.U1)]
    bool get_AllowAllPages();

    void put_AllowCurrentPage([MarshalAs(UnmanagedType.U1)] bool value);

    [return: MarshalAs(UnmanagedType.U1)]
    bool get_AllowCurrentPage();

    void put_AllowCustomSetOfPages([MarshalAs(UnmanagedType.U1)] bool value);
}

[GeneratedComInterface]
[Guid("f8a06c54-6e7c-51c5-57fd-0660c2d71513")]
internal partial interface IPrintPageRange : IInspectable
{
    int get_FirstPageNumber();

    int get_LastPageNumber();
}

/// <summary>
/// IVector&lt;HSTRING&gt;
/// </summary>
[GeneratedComInterface]
[Guid("98b9acc1-4b56-532e-ac73-03d5291cca90")]
internal partial interface IVectorString : IInspectable
{
    nint GetAt(uint index);

    uint get_Size();

    nint GetView();

    void IndexOf(nint value, out uint index, [MarshalAs(UnmanagedType.U1)] out bool found);

    void SetAt(uint index, nint item);

    void InsertAt(uint index, nint item);

    void RemoveAt(uint index);

    void Append(nint item);
}

/// <summary>
/// IVector&lt;PrintPageRange&gt;
/// </summary>
[GeneratedComInterface]
[Guid("1d314a18-2a16-542f-a986-1b7841b878aa")]
internal partial interface IVectorPrintPageRange : IInspectable
{
    IPrintPageRange GetAt(uint index);

    uint get_Size();
}

/// <summary>
/// IAsyncOperation&lt;bool&gt;
/// </summary>
[GeneratedComInterface]
[Guid("cdb5efb3-5788-509d-9be1-71ccb8a3362a")]
internal partial interface IAsyncOperationBoolean : IInspectable
{
    void put_Completed(nint handler);

    nint get_Completed();

    [return: MarshalAs(UnmanagedType.U1)]
    bool GetResults();
}

/// <summary>
/// AsyncOperationCompletedHandler&lt;bool&gt;
/// </summary>
[GeneratedComInterface]
[Guid("c1d3d1a2-ae17-5a5f-b5a2-bdcc8844889a")]
internal partial interface IAsyncOperationCompletedHandlerBoolean
{
    void Invoke(nint asyncInfo, AsyncStatus status);
}

[GeneratedComInterface]
[Guid("00000036-0000-0000-c000-000000000046")]
internal partial interface IAsyncInfo : IInspectable
{
    uint get_Id();

    AsyncStatus get_Status();

    int get_ErrorCode();
}

internal enum AsyncStatus
{
    Started = 0,
    Completed = 1,
    Canceled = 2,
    Error = 3
}
