using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace JBIG2Encoder.ImageSharp;

/// <summary>Registration of the JBIG2 encoder with ImageSharp and convenience methods.</summary>
public static class ImageSharpJbig2Extensions
{
    /// <summary>
    /// Registers the JBIG2 format and encoder with a configuration, so that <c>image.Save("page.jb2")</c> works for
    /// images using that configuration. Typically called once at startup:
    /// <code>Configuration.Default.AddJbig2Encoder();</code>
    /// </summary>
    /// <param name="configuration">The configuration to extend.</param>
    /// <param name="encoder">The encoder to register; <c>null</c> for lossless output with default settings.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public static Configuration AddJbig2Encoder(this Configuration configuration, Jbig2ImageEncoder? encoder = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Configure(new Jbig2ConfigurationModule(encoder));
        return configuration;
    }

    /// <summary>Saves the image as JBIG2 using the given encoder settings (lossless by default).</summary>
    public static void SaveAsJbig2(this Image source, string path, Jbig2ImageEncoder? encoder = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Save(path, encoder ?? new Jbig2ImageEncoder());
    }

    /// <summary>Saves the image as JBIG2 to a stream.</summary>
    public static void SaveAsJbig2(this Image source, Stream stream, Jbig2ImageEncoder? encoder = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Save(stream, encoder ?? new Jbig2ImageEncoder());
    }

    /// <summary>Saves the image as JBIG2 asynchronously.</summary>
    public static Task SaveAsJbig2Async(this Image source, string path, Jbig2ImageEncoder? encoder = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.SaveAsync(path, encoder ?? new Jbig2ImageEncoder(), cancellationToken);
    }

    /// <summary>Saves the image as JBIG2 to a stream asynchronously.</summary>
    public static Task SaveAsJbig2Async(this Image source, Stream stream, Jbig2ImageEncoder? encoder = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.SaveAsync(stream, encoder ?? new Jbig2ImageEncoder(), cancellationToken);
    }

    /// <summary>
    /// Converts the image to the bi-level bitmap the JBIG2 encoder consumes. Use this to add several images to a
    /// <see cref="Jbig2DocumentEncoder"/> or to get PDF-style separate streams. Transparent pixels count as white.
    /// The image's resolution metadata, when present, is carried over.
    /// </summary>
    /// <typeparam name="TPixel">Pixel format of the image.</typeparam>
    /// <param name="image">The source image; it is not modified.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    public static BinaryBitmap ToJbig2Bitmap<TPixel>(this Image<TPixel> image, BinarizationOptions? binarization = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        binarization ??= new BinarizationOptions();

        int width = image.Width;
        int height = image.Height;
        var gray = new byte[checked(width * height)];

        if (TryGetLayout<TPixel>(out PixelLayout layout))
        {
            CopyGray(image, gray, layout, binarization.GrayConversion);
        }
        else
        {
            // Any other pixel format (16-bit, float, alpha-only...): go through 8-bit RGBA.
            using Image<Rgba32> converted = image.CloneAs<Rgba32>();
            CopyGray(converted, gray, PixelLayout.Rgba32, binarization.GrayConversion);
        }

        GetDpi(image.Metadata, out int xDpi, out int yDpi);
        return Jbig2Images.ToBitmapFromGray8(gray, width, height, width, binarization, xDpi, yDpi);
    }

    private static void CopyGray<TPixel>(Image<TPixel> image, byte[] gray, PixelLayout layout, GrayConversion conversion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int width = image.Width;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                ReadOnlySpan<byte> row = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                GrayConverter.ConvertRow(row, gray.AsSpan(y * width, width), layout, conversion);
            }
        });
    }

    private static bool TryGetLayout<TPixel>(out PixelLayout layout)
    {
        if (typeof(TPixel) == typeof(L8)) layout = PixelLayout.Gray8;
        else if (typeof(TPixel) == typeof(La16)) layout = PixelLayout.GrayAlpha16;
        else if (typeof(TPixel) == typeof(Rgb24)) layout = PixelLayout.Rgb24;
        else if (typeof(TPixel) == typeof(Bgr24)) layout = PixelLayout.Bgr24;
        else if (typeof(TPixel) == typeof(Rgba32)) layout = PixelLayout.Rgba32;
        else if (typeof(TPixel) == typeof(Bgra32)) layout = PixelLayout.Bgra32;
        else
        {
            layout = default;
            return false;
        }
        return true;
    }

    private static void GetDpi(ImageMetadata metadata, out int xDpi, out int yDpi)
    {
        double factor = metadata.ResolutionUnits switch
        {
            PixelResolutionUnit.PixelsPerInch => 1.0,
            PixelResolutionUnit.PixelsPerCentimeter => 2.54,
            PixelResolutionUnit.PixelsPerMeter => 0.0254,
            _ => 0.0, // aspect ratio only: unknown resolution
        };

        xDpi = ToDpi(metadata.HorizontalResolution * factor);
        yDpi = ToDpi(metadata.VerticalResolution * factor);
    }

    private static int ToDpi(double value) => value >= 1 && value < 100000 ? (int)Math.Round(value) : 0;
}
