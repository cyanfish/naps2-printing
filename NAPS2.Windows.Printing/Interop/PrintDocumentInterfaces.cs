using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NAPS2.Windows.Printing.Interop;

// The interfaces between the print dialog and a document source, with the layouts and IIDs from the Windows SDK headers
// (documentsource.idl, documenttarget.h, printpreview.h and ocidl.h).

[GeneratedComInterface]
[Guid("a96bb1db-172e-4667-82b5-ad97a252318f")]
internal partial interface IPrintDocumentPageSource
{
    void GetPreviewPageCollection(nint docPackageTarget, out nint docPageCollection);

    void MakeDocument(nint printTaskOptions, nint docPackageTarget);
}

[GeneratedComInterface]
[Guid("0b31cc62-d7ec-4747-9d6e-f2537d870f2b")]
internal partial interface IPrintPreviewPageCollection
{
    void Paginate(uint currentJobPage, nint printTaskOptions);

    void MakePage(uint desiredJobPage, float width, float height);
}

[GeneratedComInterface]
[Guid("1b8efec4-3019-4c27-964e-367202156906")]
internal partial interface IPrintDocumentPackageTarget
{
    void GetPackageTargetTypes(out uint targetCount, out nint targetTypes);

    nint GetPackageTarget(in Guid guidTargetType, in Guid riid);
}

[GeneratedComInterface]
[Guid("1a6dd0ad-1e2a-4e99-a5ba-91f17818290e")]
internal partial interface IPrintPreviewDxgiPackageTarget
{
    void SetJobPageCount(PageCountType countType, uint count);

    void DrawPage(uint jobPageNumber, nint pageImage, float dpiX, float dpiY);

    void InvalidatePreview();
}

internal enum PageCountType
{
    FinalPageCount = 0,
    IntermediatePageCount = 1
}

/// <summary>
/// A dual interface, so the IDispatch methods come first.
/// </summary>
[GeneratedComInterface]
[Guid("ed90c8ad-5c34-4d05-a1ec-0e8a9b3ad7af")]
internal partial interface IPrintDocumentPackageStatusEvent
{
    [PreserveSig]
    int GetTypeInfoCount(out uint count);

    [PreserveSig]
    int GetTypeInfo(uint index, uint localeId, out nint typeInfo);

    [PreserveSig]
    int GetIDsOfNames(in Guid iid, nint names, uint nameCount, uint localeId, nint dispIds);

    [PreserveSig]
    int Invoke(int dispId, in Guid iid, uint localeId, ushort flags, nint parameters, nint result, nint exceptionInfo,
        nint argumentError);

    void PackageStatusUpdated(in PrintDocumentPackageStatus packageStatus);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrintDocumentPackageStatus
{
    public uint JobId;
    public int CurrentDocument;
    public int CurrentPage;
    public int CurrentPageTotal;
    public int Completion;
    public int PackageStatus;
}

[GeneratedComInterface]
[Guid("b196b284-bab4-101a-b69c-00aa00341d07")]
internal partial interface IConnectionPointContainer
{
    nint EnumConnectionPoints();

    IConnectionPoint FindConnectionPoint(in Guid iid);
}

[GeneratedComInterface]
[Guid("b196b286-bab4-101a-b69c-00aa00341d07")]
internal partial interface IConnectionPoint
{
    Guid GetConnectionInterface();

    nint GetConnectionPointContainer();

    uint Advise(nint sink);

    void Unadvise(uint cookie);
}
