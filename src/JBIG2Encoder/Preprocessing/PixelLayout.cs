using System;

namespace JBIG2Encoder;

/// <summary>Memory layout of an interleaved 8-bit-per-channel pixel buffer.</summary>
public enum PixelLayout
{
    /// <summary>One byte per pixel, 0 = black.</summary>
    Gray8,

    /// <summary>Gray + alpha, two bytes per pixel.</summary>
    GrayAlpha16,

    /// <summary>R, G, B.</summary>
    Rgb24,

    /// <summary>B, G, R.</summary>
    Bgr24,

    /// <summary>R, G, B, A.</summary>
    Rgba32,

    /// <summary>B, G, R, A.</summary>
    Bgra32,
}

/// <summary>Conversion of interleaved pixel buffers to 8-bit gray.</summary>
public static class GrayConverter
{
    /// <summary>Bytes per pixel of the given layout.</summary>
    public static int BytesPerPixel(PixelLayout layout) => layout switch
    {
        PixelLayout.Gray8 => 1,
        PixelLayout.GrayAlpha16 => 2,
        PixelLayout.Rgb24 or PixelLayout.Bgr24 => 3,
        PixelLayout.Rgba32 or PixelLayout.Bgra32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>
    /// Converts a pixel buffer to tightly packed 8-bit gray (<c>width * height</c> bytes). Pixels with an alpha
    /// channel are composited over white, so transparent areas become background.
    /// </summary>
    /// <param name="pixels">Interleaved source pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per source row (at least <c>width * BytesPerPixel</c>).</param>
    /// <param name="layout">Channel layout of the source.</param>
    /// <param name="conversion">How color is reduced to gray.</param>
    public static byte[] ToGray8(ReadOnlySpan<byte> pixels, int width, int height, int stride, PixelLayout layout,
        GrayConversion conversion = GrayConversion.Luma)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int bpp = BytesPerPixel(layout);
        if (stride < width * bpp) throw new ArgumentOutOfRangeException(nameof(stride));
        if (pixels.Length < (long)stride * (height - 1) + (long)width * bpp)
        {
            throw new ArgumentException("Pixel buffer is too short for the given dimensions.", nameof(pixels));
        }

        var gray = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            ConvertRow(pixels.Slice(y * stride, width * bpp), gray.AsSpan(y * width, width), layout, conversion);
        }
        return gray;
    }

    /// <summary>
    /// Converts one row of interleaved pixels to gray. <paramref name="destination"/> determines the number of pixels.
    /// Pixels with an alpha channel are composited over white.
    /// </summary>
    /// <param name="source">Interleaved source pixels of the row.</param>
    /// <param name="destination">Receives one gray byte per pixel.</param>
    /// <param name="layout">Channel layout of the source.</param>
    /// <param name="conversion">How color is reduced to gray.</param>
    public static void ConvertRow(ReadOnlySpan<byte> source, Span<byte> destination, PixelLayout layout,
        GrayConversion conversion = GrayConversion.Luma)
    {
        int width = destination.Length;
        int bpp = BytesPerPixel(layout);
        if (source.Length < width * bpp) throw new ArgumentException("Source row is too short.", nameof(source));

        ReadOnlySpan<byte> src = source;
        Span<byte> dst = destination;
        switch (layout)
        {
            case PixelLayout.Gray8:
                src.Slice(0, width).CopyTo(dst);
                break;

            case PixelLayout.GrayAlpha16:
                for (int x = 0; x < width; x++) dst[x] = OverWhite(src[x * 2], src[x * 2 + 1]);
                break;

            case PixelLayout.Rgb24:
                for (int x = 0; x < width; x++) dst[x] = Reduce(src[x * 3], src[x * 3 + 1], src[x * 3 + 2], conversion);
                break;

            case PixelLayout.Bgr24:
                for (int x = 0; x < width; x++) dst[x] = Reduce(src[x * 3 + 2], src[x * 3 + 1], src[x * 3], conversion);
                break;

            case PixelLayout.Rgba32:
                for (int x = 0; x < width; x++)
                    dst[x] = OverWhite(Reduce(src[x * 4], src[x * 4 + 1], src[x * 4 + 2], conversion), src[x * 4 + 3]);
                break;

            case PixelLayout.Bgra32:
                for (int x = 0; x < width; x++)
                    dst[x] = OverWhite(Reduce(src[x * 4 + 2], src[x * 4 + 1], src[x * 4], conversion), src[x * 4 + 3]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(layout));
        }
    }

    private static byte Reduce(byte r, byte g, byte b, GrayConversion conversion)
    {
        if (conversion == GrayConversion.Green) return g;
        // 0.299 R + 0.587 G + 0.114 B in 16.16 fixed point, rounded.
        return (byte)((19595 * r + 38470 * g + 7471 * b + 32768) >> 16);
    }

    private static byte OverWhite(byte gray, byte alpha)
    {
        if (alpha == 255) return gray;
        return (byte)((gray * alpha + 255 * (255 - alpha) + 127) / 255);
    }
}
