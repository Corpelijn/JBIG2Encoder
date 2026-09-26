using NetVips;

namespace JBIG2Encoder.NetVips;

/// <summary>
/// JBIG2 output for NetVips images. libvips has no JBIG2 saver and NetVips cannot add one, so these extension
/// methods are named after the generated savers (<c>Jbig2save</c>, <c>Jbig2saveBuffer</c>, <c>Jbig2saveStream</c>) and
/// do the work in managed code.
/// </summary>
public static class NetVipsJbig2Extensions
{
    /// <summary>
    /// Converts the image to the bi-level bitmap the JBIG2 encoder consumes (use it to build multi-page documents with
    /// <see cref="Jbig2DocumentEncoder"/>). The image is converted to 8-bit sRGB (or 8-bit gray) by libvips and flattened
    /// onto white if it has an alpha channel. The image's resolution metadata is carried over.
    /// </summary>
    /// <param name="image">The source; it is not modified or disposed.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    public static BinaryBitmap ToJbig2Bitmap(this Image image, BinarizationOptions? binarization = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        Image work = image;
        Image? owned = null;
        try
        {
            bool plainGray = work.Bands == 1 && work.Format == Enums.BandFormat.Uchar;
            if (!plainGray)
            {
                // Colorspace conversion also brings 16-bit, float, CMYK, Lab, indexed... to 8-bit sRGB.
                owned = work.Colourspace(Enums.Interpretation.Srgb);
                work = owned;
                if (work.HasAlpha())
                {
                    Image flattened = work.Flatten(new[] { 255.0, 255.0, 255.0 });
                    owned.Dispose();
                    owned = flattened;
                    work = flattened;
                }
            }

            int width = work.Width;
            int height = work.Height;
            byte[] pixels = work.WriteToMemory<byte>();

            PixelLayout layout = work.Bands switch
            {
                1 => PixelLayout.Gray8,
                3 => PixelLayout.Rgb24,
                4 => PixelLayout.Rgba32,
                _ => throw new NotSupportedException($"Cannot convert an image with {work.Bands} bands to black and white."),
            };

            // libvips stores resolution in pixels per millimetre; 1.0 is its "not set" default.
            int xDpi = ToDpi(image.Xres);
            int yDpi = ToDpi(image.Yres);
            return Jbig2Images.ToBitmap(pixels, width, height, width * GrayConverter.BytesPerPixel(layout), layout, binarization, xDpi, yDpi);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Binarizes and encodes the image as a single JBIG2 page (lossless unless the options say otherwise).
    /// </summary>
    /// <param name="image">The source; it is not modified or disposed.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <returns>The streams; call <see cref="Jbig2Document.ToFile"/> for a standalone file.</returns>
    public static Jbig2Document EncodeJbig2(this Image image, Jbig2ImageOptions? options = null)
    {
        options ??= new Jbig2ImageOptions();
        return Jbig2Encoder.Encode(image.ToJbig2Bitmap(options.Binarization), options.Encoding);
    }

    /// <summary>Encodes the image as JBIG2 and returns the bytes (a standalone file by default).</summary>
    /// <param name="image">The source; it is not modified or disposed.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    public static byte[] Jbig2saveBuffer(this Image image, Jbig2ImageOptions? options = null) =>
        image.EncodeJbig2(options).ToSingleStream();

    /// <summary>Encodes the image as JBIG2 and writes it to a stream.</summary>
    /// <param name="image">The source; it is not modified or disposed.</param>
    /// <param name="stream">The destination.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    public static void Jbig2saveStream(this Image image, Stream stream, Jbig2ImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] data = image.Jbig2saveBuffer(options);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>Encodes the image as JBIG2 and writes it to a file.</summary>
    /// <param name="image">The source; it is not modified or disposed.</param>
    /// <param name="filename">The destination file.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    public static void Jbig2save(this Image image, string filename, Jbig2ImageOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        File.WriteAllBytes(filename, image.Jbig2saveBuffer(options));
    }

    private static int ToDpi(double pixelsPerMillimetre)
    {
        // 1.0 px/mm (25.4 dpi) is libvips' default for "unspecified".
        if (Math.Abs(pixelsPerMillimetre - 1.0) < 1e-9) return 0;
        double dpi = pixelsPerMillimetre * 25.4;
        return dpi >= 1 && dpi < 100000 ? (int)Math.Round(dpi) : 0;
    }
}
