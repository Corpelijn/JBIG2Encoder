namespace JBIG2Encoder;

/// <summary>How gray or color pixels are turned into a black/white decision.</summary>
public enum BinarizationMode
{
    /// <summary>
    /// The default of the reference encoder (jbig2enc "local" mode): the background is estimated per tile
    /// and normalized to white first (this removes shading, scanner gradients and paper tint), then a
    /// fixed threshold is applied. Best for scans and photographed documents.
    /// </summary>
    AdaptiveBackground,

    /// <summary>A single fixed threshold over the whole image (jbig2enc <c>-G</c>). Best for clean, rendered input.</summary>
    Global,
}

/// <summary>How color pixels are reduced to gray before binarization.</summary>
public enum GrayConversion
{
    /// <summary>ITU-R BT.601 luma (0.299 R + 0.587 G + 0.114 B). Recommended.</summary>
    Luma,

    /// <summary>Only the green channel, which is what the reference encoder does (fast, not perceptual).</summary>
    Green,
}

/// <summary>Options for <see cref="Binarizer"/>.</summary>
public sealed class BinarizationOptions
{
    /// <summary>Default fixed threshold used after background normalization (jbig2enc <c>-T</c> local default).</summary>
    public const int DefaultAdaptiveThreshold = 200;

    /// <summary>Default fixed threshold in <see cref="BinarizationMode.Global"/> mode (jbig2enc <c>-G</c> default).</summary>
    public const int DefaultGlobalThreshold = 128;

    /// <summary>Default: <see cref="BinarizationMode.AdaptiveBackground"/>.</summary>
    public BinarizationMode Mode { get; set; } = BinarizationMode.AdaptiveBackground;

    /// <summary>
    /// Pixels darker than this value (0..255) become black. -1 (the default) selects
    /// <see cref="DefaultAdaptiveThreshold"/> or <see cref="DefaultGlobalThreshold"/> depending on <see cref="Mode"/>.
    /// </summary>
    public int Threshold { get; set; } = -1;

    /// <summary>
    /// Upsampling before thresholding: 1 (none), 2 or 4. The gray image is linearly interpolated and then
    /// thresholded, which yields smoother glyph edges at the cost of a 4x / 16x larger bitmap.
    /// </summary>
    public int Upsample { get; set; } = 1;

    /// <summary>Gray conversion for color input. Default: <see cref="GrayConversion.Luma"/>.</summary>
    public GrayConversion GrayConversion { get; set; } = GrayConversion.Luma;

    /// <summary>
    /// Gamma applied when cleaning the background in <see cref="BinarizationMode.AdaptiveBackground"/>
    /// (below 1.0 increases contrast for light text). Default 1.0.
    /// </summary>
    public float Gamma { get; set; } = 1.0f;

    /// <summary>After background normalization, values at or below this become pure black. Default 90.</summary>
    public int BlackValue { get; set; } = 90;

    /// <summary>After background normalization, values at or above this become pure white. Must not exceed 200. Default 190.</summary>
    public int WhiteValue { get; set; } = 190;

    internal int EffectiveThreshold =>
        Threshold >= 0 ? Threshold : Mode == BinarizationMode.Global ? DefaultGlobalThreshold : DefaultAdaptiveThreshold;
}
