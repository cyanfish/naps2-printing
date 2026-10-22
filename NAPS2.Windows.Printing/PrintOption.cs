namespace NAPS2.Windows.Printing;

/// <summary>
/// A standard option in the print dialog. See <see cref="PrintDialogOptions.DisplayedOptions"/>.
/// </summary>
public enum PrintOption
{
    /// <summary>The number of copies.</summary>
    Copies,

    /// <summary>Portrait or landscape.</summary>
    Orientation,

    /// <summary>Color or grayscale.</summary>
    ColorMode,

    /// <summary>All pages, the current page or a custom range.</summary>
    PageRange,

    /// <summary>The paper size (e.g. Letter or A4).</summary>
    PaperSize,

    /// <summary>The paper type (e.g. plain or photo paper).</summary>
    PaperType,

    /// <summary>One-sided or two-sided printing.</summary>
    Duplex,

    /// <summary>Whether copies are collated.</summary>
    Collation,

    /// <summary>The print quality.</summary>
    Quality,

    /// <summary>Whether to print without margins.</summary>
    Borderless,

    /// <summary>Stapling.</summary>
    Staple,

    /// <summary>Hole punching.</summary>
    HolePunch,

    /// <summary>Binding.</summary>
    Binding
}
