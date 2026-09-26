using System;
using JBIG2Encoder.Preprocessing;

namespace JBIG2Encoder;

/// <summary>
/// Converts gray or color images to the bi-level bitmaps the JBIG2 encoder consumes. JBIG2 only stores 1-bit
/// images, so whatever produces the black/white decision determines the final quality; the default reproduces
/// the behavior of the jbig2enc command line tool (background cleaning + fixed threshold).
/// </summary>
public static class Binarizer
{
    /// <summary>Binarizes an 8-bit grayscale image.</summary>
    /// <param name="gray">Pixels, 0 = black, 255 = white.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per row (at least <paramref name="width"/>).</param>
    /// <param name="options">Options; <c>null</c> for the defaults.</param>
    public static BinaryBitmap FromGray8(ReadOnlySpan<byte> gray, int width, int height, int stride, BinarizationOptions? options = null)
    {
        options ??= new BinarizationOptions();
        Validate(options);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width) throw new ArgumentOutOfRangeException(nameof(stride));
        if (gray.Length < (long)stride * (height - 1) + width)
        {
            throw new ArgumentException("Pixel buffer is too short for the given dimensions.", nameof(gray));
        }

        byte[]? cleaned = null;
        ReadOnlySpan<byte> source = gray;
        int sourceStride = stride;
        if (options.Mode == BinarizationMode.AdaptiveBackground)
        {
            cleaned = BackgroundNormalizer.CleanToWhite(gray, width, height, stride, options.Gamma, options.BlackValue, options.WhiteValue);
            source = cleaned;
            sourceStride = width;
        }

        return Threshold(source, width, height, sourceStride, options.EffectiveThreshold, options.Upsample);
    }

    /// <summary>Binarizes an interleaved color (or gray) image.</summary>
    public static BinaryBitmap FromPixels(ReadOnlySpan<byte> pixels, int width, int height, int stride, PixelLayout layout, BinarizationOptions? options = null)
    {
        options ??= new BinarizationOptions();
        byte[] gray = GrayConverter.ToGray8(pixels, width, height, stride, layout, options.GrayConversion);
        return FromGray8(gray, width, height, width, options);
    }

    private static void Validate(BinarizationOptions o)
    {
        if (o.Upsample is not (1 or 2 or 4)) throw new ArgumentException("Upsample must be 1, 2 or 4.", nameof(o));
        if (o.Threshold > 255) throw new ArgumentException("Threshold must be in 0..255 (or -1 for the default).", nameof(o));
        if (o.Gamma <= 0f) throw new ArgumentException("Gamma must be greater than zero.", nameof(o));
        if (o.BlackValue < 0 || o.WhiteValue > 255 || o.BlackValue >= o.WhiteValue)
        {
            throw new ArgumentException("BlackValue must be less than WhiteValue, both within 0..255.", nameof(o));
        }
    }

    /// <summary>Pixels below <paramref name="threshold"/> become black; optionally after linear upsampling.</summary>
    private static BinaryBitmap Threshold(ReadOnlySpan<byte> gray, int w, int h, int stride, int threshold, int factor)
    {
        if (factor == 1)
        {
            var bmp = new BinaryBitmap(w, h);
            for (int y = 0; y < h; y++)
            {
                ReadOnlySpan<byte> src = gray.Slice(y * stride, w);
                Span<uint> row = bmp.Row(y);
                for (int x = 0; x < w; x++)
                {
                    if (src[x] < threshold) row[x >> 5] |= 0x80000000u >> (x & 31);
                }
            }
            return bmp;
        }

        int dw = checked(w * factor);
        int dh = checked(h * factor);
        var result = new BinaryBitmap(dw, dh);

        // Horizontally interpolated source rows, cached for the current pair.
        var rowA = new int[dw];
        var rowB = new int[dw];

        static void Interpolate(ReadOnlySpan<byte> gray, int w, int stride, int factor, int sy, int[] target)
        {
            ReadOnlySpan<byte> src = gray.Slice(sy * stride, w);
            for (int sx = 0; sx < w; sx++)
            {
                int a = src[sx];
                int b = src[Math.Min(sx + 1, w - 1)];
                for (int k = 0; k < factor; k++) target[sx * factor + k] = ((factor - k) * a + k * b) * 256 / factor;
            }
        }

        for (int sy = 0; sy < h; sy++)
        {
            Interpolate(gray, w, stride, factor, sy, rowA);
            Interpolate(gray, w, stride, factor, Math.Min(sy + 1, h - 1), rowB);
            for (int k = 0; k < factor; k++)
            {
                Span<uint> row = result.Row(sy * factor + k);
                for (int x = 0; x < dw; x++)
                {
                    int v = ((factor - k) * rowA[x] + k * rowB[x]) / factor; // gray scaled by 256
                    if (v < threshold * 256) row[x >> 5] |= 0x80000000u >> (x & 31);
                }
            }
        }
        return result;
    }
}
