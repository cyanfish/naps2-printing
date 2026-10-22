# NAPS2.Windows.Printing

[![NuGet](https://img.shields.io/nuget/v/NAPS2.Windows.Printing)](https://www.nuget.org/packages/NAPS2.Windows.Printing/)

NAPS2.Windows.Printing prints images with the Windows print dialog, including its print preview.

Desktop apps that use the classic print APIs (e.g. the WinForms or WPF `PrintDialog`) get the new Windows 11 print
dialog, but it shows "This app doesn't support print preview". This package provides the pages to the dialog the same
way UWP and WinUI apps do, so the preview works. It can be used from WinForms, WPF or any other .NET app with a window.

- Each page is an image, rotated (if needed) to match the paper orientation and scaled to fit the paper
- Supports printing all pages, the current page in the preview or a custom page range
- Requires Windows 10 or later and .NET 8 or later
- No dependencies (it calls the Windows APIs directly)

## WinForms example

```c#
// Pages are provided as 32-bit BGRA pixel data
PageImage GetPage(int index)
{
    var bitmap = images[index];
    var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
        PixelFormat.Format32bppArgb);
    return new PageImage(data.Width, data.Height, data.Stride, data.Scan0, () => bitmap.UnlockBits(data));
}

if (WindowsPrintDialog.IsSupported)
{
    bool printed = await WindowsPrintDialog.ShowAsync(form.Handle, "My Document", images.Count, GetPage);
}
```

## WPF example

```c#
// Pages are provided as 32-bit BGRA pixel data. The images need to be frozen, as GetPage is called on background
// threads.
PageImage GetPage(int index)
{
    var bitmap = new FormatConvertedBitmap(images[index], PixelFormats.Bgra32, null, 0);
    int stride = bitmap.PixelWidth * 4;
    int size = stride * bitmap.PixelHeight;
    nint pixels = Marshal.AllocHGlobal(size);
    bitmap.CopyPixels(Int32Rect.Empty, pixels, size, stride);
    return new PageImage(bitmap.PixelWidth, bitmap.PixelHeight, stride, pixels, () => Marshal.FreeHGlobal(pixels));
}

if (WindowsPrintDialog.IsSupported)
{
    var handle = new WindowInteropHelper(window).Handle;
    bool printed = await WindowsPrintDialog.ShowAsync(handle, "My Document", images.Count, GetPage);
}
```

`ShowAsync` must be called on the window's UI thread. It returns true if the document was sent to the printer, or
false if the user cancelled.

## Options

```c#
var options = new PrintDialogOptions
{
    // The page the preview starts on, which is the page printed if the user chooses "Current page"
    StartPage = selectedPages.Min(),
    // The pages to print by default, shown as a custom page range (null for all pages)
    InitialPages = selectedPages,
    // The paper orientation that the dialog starts with (null for the printer's default)
    Orientation = PrintOrientation.Landscape,
    // Whether pages are rotated to match the paper orientation (true by default)
    RotatePages = true,
    // The options shown in the main part of the dialog (null for the defaults)
    DisplayedOptions = [PrintOption.Copies, PrintOption.Orientation, PrintOption.PageRange, PrintOption.PaperSize]
};
bool printed = await WindowsPrintDialog.ShowAsync(form.Handle, "My Document", images.Count, GetPage, options);
```

The page range is always shown. Options that the printer doesn't support (e.g. color mode for a black and white
printer) aren't shown.

## Notes

`GetPage` is called on background threads (one call at a time), and can be called more than once for the same page.
Each `PageImage` is disposed once it has been drawn. If `GetPage` throws, `ShowAsync` rethrows the exception.
