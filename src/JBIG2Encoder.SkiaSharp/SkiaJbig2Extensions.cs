using SkiaSharp;

namespace JBIG2Encoder.SkiaSharp;

/// <summary>
/// JBIG2 encoding for SkiaSharp images. SkiaSharp's <c>SKEncodedImageFormat</c> is a closed set, so JBIG2 cannot be
/// registered as one of its formats; these extension methods are the integration point instead.
/// </summary>
public static class SkiaJbig2Extensions
{
    #region ToJbig2Bitmap

    /// <summary>
    /// Converts a bitmap to the bi-level bitmap the JBIG2 encoder consumes (use it to build multi-page documents with
    /// <see cref="Jbig2DocumentEncoder"/>). Transparent pixels count as white.
    /// </summary>
    /// <param name="bitmap">The source; it is not modified.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    /// <param name="dpi">Resolution to record in the output (SkiaSharp bitmaps carry none); 0 = unknown.</param>
    public static BinaryBitmap ToJbig2Bitmap(this SKBitmap bitmap, BinarizationOptions? binarization = null, int dpi = 0)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using SKPixmap pixmap = bitmap.PeekPixels()
            ?? throw new InvalidOperationException("The bitmap has no accessible pixels.");
        return pixmap.ToJbig2Bitmap(binarization, dpi);
    }

    /// <inheritdoc cref="ToJbig2Bitmap(SKBitmap, BinarizationOptions?, int)"/>
    /// <param name="image">The source; it is not modified.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    /// <param name="dpi">Resolution to record in the output; 0 = unknown.</param>
    public static BinaryBitmap ToJbig2Bitmap(this SKImage image, BinarizationOptions? binarization = null, int dpi = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        using SKPixmap? peeked = image.PeekPixels();
        if (peeked != null) return peeked.ToJbig2Bitmap(binarization, dpi);

        // Encoded, texture-backed or otherwise non-raster image: decode into a bitmap first.
        using SKBitmap bitmap = SKBitmap.FromImage(image)
            ?? throw new InvalidOperationException("The image could not be read into a bitmap.");
        return bitmap.ToJbig2Bitmap(binarization, dpi);
    }

    /// <inheritdoc cref="ToJbig2Bitmap(SKBitmap, BinarizationOptions?, int)"/>
    /// <param name="pixmap">The source; it is not modified.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    /// <param name="dpi">Resolution to record in the output; 0 = unknown.</param>
    public static BinaryBitmap ToJbig2Bitmap(this SKPixmap pixmap, BinarizationOptions? binarization = null, int dpi = 0)
    {
        ArgumentNullException.ThrowIfNull(pixmap);
        if (pixmap.Width <= 0 || pixmap.Height <= 0) throw new ArgumentException("The image is empty.", nameof(pixmap));

        SKColorType colorType = pixmap.ColorType;
        SKAlphaType alphaType = pixmap.AlphaType;

        // The pixel span covers width * height * bytes-per-pixel bytes, so rows must be tightly packed to read it
        // directly (a subset of a larger bitmap has padded rows and takes the conversion path below).
        bool tightRows = pixmap.RowBytes == pixmap.Width * pixmap.BytesPerPixel;

        // Formats the encoder reads directly. Premultiplied alpha is converted first because compositing over white
        // needs straight (unpremultiplied) color values.
        bool premultiplied = alphaType == SKAlphaType.Premul;
        PixelLayout? direct = null;
        if (colorType == SKColorType.Gray8) direct = PixelLayout.Gray8;
        else if (colorType == SKColorType.Rgba8888 && !premultiplied) direct = PixelLayout.Rgba32;
        else if (colorType == SKColorType.Bgra8888 && !premultiplied) direct = PixelLayout.Bgra32;

        if (direct.HasValue && tightRows)
        {
            return Jbig2Images.ToBitmap(pixmap.GetPixelSpan(), pixmap.Width, pixmap.Height, pixmap.RowBytes, direct.Value, binarization, dpi, dpi);
        }

        // Everything else (premultiplied, 565, F16, alpha-only...): let Skia convert to straight-alpha RGBA.
        var info = new SKImageInfo(pixmap.Width, pixmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var converted = new SKBitmap(info);
        using SKPixmap target = converted.PeekPixels() ?? throw new InvalidOperationException("Could not allocate a conversion buffer.");
        if (!pixmap.ReadPixels(target))
        {
            throw new InvalidOperationException($"Skia cannot convert {colorType}/{alphaType} pixels to RGBA.");
        }
        return Jbig2Images.ToBitmap(target.GetPixelSpan(), target.Width, target.Height, target.RowBytes, PixelLayout.Rgba32, binarization, dpi, dpi);
    }

    #endregion

    #region EncodeJbig2

    /// <summary>
    /// Binarizes and encodes the bitmap as a single JBIG2 page (lossless unless the options say otherwise).
    /// </summary>
    /// <param name="bitmap">The source; it is not modified.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown (or <see cref="Jbig2Options.Dpi"/>).</param>
    /// <returns>The streams; call <see cref="Jbig2Document.ToFile"/> for a standalone file.</returns>
    public static Jbig2Document EncodeJbig2(this SKBitmap bitmap, Jbig2ImageOptions? options = null, int dpi = 0)
    {
        options ??= new Jbig2ImageOptions();
        return Jbig2Encoder.Encode(bitmap.ToJbig2Bitmap(options.Binarization, dpi), options.Encoding);
    }

    /// <inheritdoc cref="EncodeJbig2(SKBitmap, Jbig2ImageOptions?, int)"/>
    /// <param name="image">The source; it is not modified.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static Jbig2Document EncodeJbig2(this SKImage image, Jbig2ImageOptions? options = null, int dpi = 0)
    {
        options ??= new Jbig2ImageOptions();
        return Jbig2Encoder.Encode(image.ToJbig2Bitmap(options.Binarization, dpi), options.Encoding);
    }

    /// <inheritdoc cref="EncodeJbig2(SKBitmap, Jbig2ImageOptions?, int)"/>
    /// <param name="pixmap">The source; it is not modified.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static Jbig2Document EncodeJbig2(this SKPixmap pixmap, Jbig2ImageOptions? options = null, int dpi = 0)
    {
        options ??= new Jbig2ImageOptions();
        return Jbig2Encoder.Encode(pixmap.ToJbig2Bitmap(options.Binarization, dpi), options.Encoding);
    }

    /// <summary>
    /// Encodes the bitmap and returns it as one byte array (a standalone JBIG2 file by default). Throws for the
    /// combination of lossy output and <see cref="Jbig2Output.PdfEmbedded"/>, which needs two streams; use
    /// <see cref="EncodeJbig2(SKBitmap, Jbig2ImageOptions?, int)"/> then.
    /// </summary>
    /// <param name="bitmap">The source; it is not modified.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static byte[] EncodeJbig2ToBytes(this SKBitmap bitmap, Jbig2ImageOptions? options = null, int dpi = 0) =>
        bitmap.EncodeJbig2(options, dpi).ToSingleStream();

    /// <inheritdoc cref="EncodeJbig2ToBytes(SKBitmap, Jbig2ImageOptions?, int)"/>
    /// <param name="image">The source; it is not modified.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static byte[] EncodeJbig2ToBytes(this SKImage image, Jbig2ImageOptions? options = null, int dpi = 0) =>
        image.EncodeJbig2(options, dpi).ToSingleStream();

    #endregion

    #region SaveJbig2

    /// <summary>Encodes the bitmap and writes it to a stream (see <see cref="EncodeJbig2ToBytes(SKBitmap, Jbig2ImageOptions?, int)"/>).</summary>
    /// <param name="bitmap">The source; it is not modified.</param>
    /// <param name="stream">The destination.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static void SaveJbig2(this SKBitmap bitmap, Stream stream, Jbig2ImageOptions? options = null, int dpi = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] data = bitmap.EncodeJbig2ToBytes(options, dpi);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>Encodes the bitmap and writes it to a file.</summary>
    /// <param name="bitmap">The source; it is not modified.</param>
    /// <param name="path">The destination file.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="dpi">Resolution to record; 0 = unknown.</param>
    public static void SaveJbig2(this SKBitmap bitmap, string path, Jbig2ImageOptions? options = null, int dpi = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllBytes(path, bitmap.EncodeJbig2ToBytes(options, dpi));
    }

    #endregion
}
