using System;

namespace JBIG2Encoder;

/// <summary>How the encoded JBIG2 data is packaged.</summary>
public enum Jbig2Output
{
    /// <summary>
    /// A complete, standalone JBIG2 file (<c>.jb2</c> / <c>.jbig2</c>): file header, segments, end-of-page
    /// and end-of-file segments.
    /// </summary>
    StandaloneFile,

    /// <summary>
    /// Segments only, without file header, end-of-page or end-of-file segments. This is what a PDF
    /// <c>/JBIG2Decode</c> stream expects; shared symbols go into a separate globals stream.
    /// </summary>
    PdfEmbedded,
}

/// <summary>Which JBIG2 coding tools are used.</summary>
public enum Jbig2Mode
{
    /// <summary>
    /// Generic region coding: the image is coded pixel by pixel with an arithmetic coder. Decoding yields
    /// exactly the input bitmap. This is the default and the only mode that cannot alter what a page says.
    /// </summary>
    Lossless,

    /// <summary>
    /// Symbol dictionary and text region coding: shapes that look alike (typically letters) are stored once and
    /// every occurrence is replaced by that stored shape. Much smaller for text pages, but <b>lossy</b>: with too
    /// low a similarity threshold, different characters (e.g. 6 and 8) can be substituted for one another. Review
    /// results before using it for documents where that matters.
    /// </summary>
    Lossy,
}

/// <summary>Options controlling JBIG2 encoding.</summary>
public sealed class Jbig2Options
{
    /// <summary>Default classification threshold of the reference encoder (jbig2enc <c>-t</c>).</summary>
    public const float DefaultSymbolThreshold = 0.92f;

    /// <summary>Default classification weight of the reference encoder (jbig2enc <c>-w</c>).</summary>
    public const float DefaultSymbolWeight = 0.5f;

    /// <summary>Lossless generic region coding (default) or lossy symbol coding.</summary>
    public Jbig2Mode Mode { get; set; } = Jbig2Mode.Lossless;

    /// <summary>Packaging of the output. Default: <see cref="Jbig2Output.StandaloneFile"/>.</summary>
    public Jbig2Output Output { get; set; } = Jbig2Output.StandaloneFile;

    /// <summary>
    /// Use TPGD (typical prediction of duplicate lines) in the generic region coder. It cuts encoding
    /// time roughly in half at the cost of very slightly larger output. Only affects
    /// <see cref="Jbig2Mode.Lossless"/>. Default: false.
    /// </summary>
    public bool DuplicateLineRemoval { get; set; }

    /// <summary>
    /// Resolution written to the page information segment, in dots per inch. 0 (the default) uses the
    /// resolution stored in the bitmap, or "unknown" when the bitmap has none. The value is converted to the
    /// pixels-per-metre unit the JBIG2 format requires.
    /// </summary>
    public int Dpi { get; set; }

    /// <summary>
    /// <see cref="Jbig2Mode.Lossy"/> only: correlation threshold (0.4 – 0.98) above which two shapes are treated as
    /// the same symbol. Higher is safer (fewer substitutions, larger files). Default 0.92; use 0.95 or more when
    /// look-alike glyphs matter.
    /// </summary>
    public float SymbolThreshold { get; set; } = DefaultSymbolThreshold;

    /// <summary>
    /// <see cref="Jbig2Mode.Lossy"/> only: (0 – 1) raises the threshold for thick, mostly black shapes, which
    /// otherwise correlate well with many different glyphs. Default 0.5.
    /// </summary>
    public float SymbolWeight { get; set; } = DefaultSymbolWeight;

    /// <summary>
    /// <see cref="Jbig2Mode.Lossy"/> only: after classification, merge symbols that a stricter shape comparison
    /// considers equivalent, to shrink the dictionary (jbig2enc <c>-a</c>). Default: false.
    /// </summary>
    public bool AutoThreshold { get; set; }

    /// <summary>
    /// <see cref="Jbig2Mode.Lossy"/> with <see cref="AutoThreshold"/>: only compare symbols that share a cheap hash.
    /// Much faster for large dictionaries. Default: true.
    /// </summary>
    public bool AutoThresholdUsesHash { get; set; } = true;

    internal int ResolveXDpi(BinaryBitmap bitmap) => Dpi > 0 ? Dpi : bitmap.XResolutionDpi;

    internal int ResolveYDpi(BinaryBitmap bitmap) => Dpi > 0 ? Dpi : bitmap.YResolutionDpi;

    internal bool FullHeaders => Output == Jbig2Output.StandaloneFile;

    internal Jbig2Options Clone() => (Jbig2Options)MemberwiseClone();

    internal void Validate()
    {
        if (Dpi < 0 || Dpi > 100000) throw new ArgumentOutOfRangeException(nameof(Dpi), "Dpi must be between 0 and 100000.");
        if (!(SymbolThreshold >= 0.4f && SymbolThreshold <= 0.98f))
        {
            throw new ArgumentOutOfRangeException(nameof(SymbolThreshold), "SymbolThreshold must be between 0.4 and 0.98.");
        }
        if (!(SymbolWeight >= 0f && SymbolWeight <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(SymbolWeight), "SymbolWeight must be between 0 and 1.");
        }
    }
}
