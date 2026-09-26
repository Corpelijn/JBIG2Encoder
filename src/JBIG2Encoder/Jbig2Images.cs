using System;

namespace JBIG2Encoder;

/// <summary>Everything needed to turn a gray or color image into JBIG2: how to binarize it and how to encode it.</summary>
public sealed class Jbig2ImageOptions
{
    /// <summary>JBIG2 coding options (lossless or lossy, packaging, resolution...).</summary>
    public Jbig2Options Encoding { get; set; } = new();

    /// <summary>How pixels become black or white.</summary>
    public BinarizationOptions Binarization { get; set; } = new();
}

/// <summary>Convenience entry points from raw pixel buffers; the imaging-library integrations build on these.</summary>
public static class Jbig2Images
{
    /// <summary>Converts an interleaved pixel buffer to a bi-level bitmap ready for encoding.</summary>
    /// <param name="pixels">Pixel data.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <param name="layout">Channel layout.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    /// <param name="xDpi">Horizontal resolution to record, or 0 if unknown.</param>
    /// <param name="yDpi">Vertical resolution to record, or 0 if unknown.</param>
    public static BinaryBitmap ToBitmap(ReadOnlySpan<byte> pixels, int width, int height, int stride, PixelLayout layout,
        BinarizationOptions? binarization = null, int xDpi = 0, int yDpi = 0)
    {
        BinaryBitmap bitmap = Binarizer.FromPixels(pixels, width, height, stride, layout, binarization);
        if (xDpi > 0) bitmap.XResolutionDpi = xDpi * bitmap.Width / width;   // upsampling multiplies the resolution too
        if (yDpi > 0) bitmap.YResolutionDpi = yDpi * bitmap.Height / height;
        return bitmap;
    }

    /// <summary>Converts an 8-bit grayscale buffer to a bi-level bitmap ready for encoding.</summary>
    /// <param name="gray">Gray pixels, 0 = black.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <param name="binarization">Binarization options; <c>null</c> for the defaults.</param>
    /// <param name="xDpi">Horizontal resolution to record, or 0 if unknown.</param>
    /// <param name="yDpi">Vertical resolution to record, or 0 if unknown.</param>
    public static BinaryBitmap ToBitmapFromGray8(ReadOnlySpan<byte> gray, int width, int height, int stride,
        BinarizationOptions? binarization = null, int xDpi = 0, int yDpi = 0)
    {
        BinaryBitmap bitmap = Binarizer.FromGray8(gray, width, height, stride, binarization);
        if (xDpi > 0) bitmap.XResolutionDpi = xDpi * bitmap.Width / width;
        if (yDpi > 0) bitmap.YResolutionDpi = yDpi * bitmap.Height / height;
        return bitmap;
    }

    /// <summary>Binarizes and encodes an interleaved pixel buffer as a single page.</summary>
    /// <param name="pixels">Pixel data.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <param name="layout">Channel layout.</param>
    /// <param name="options">Binarization and encoding options; <c>null</c> for lossless output as a standalone file.</param>
    /// <param name="xDpi">Horizontal resolution to record, or 0 if unknown.</param>
    /// <param name="yDpi">Vertical resolution to record, or 0 if unknown.</param>
    public static Jbig2Document Encode(ReadOnlySpan<byte> pixels, int width, int height, int stride, PixelLayout layout,
        Jbig2ImageOptions? options = null, int xDpi = 0, int yDpi = 0)
    {
        options ??= new Jbig2ImageOptions();
        BinaryBitmap bitmap = ToBitmap(pixels, width, height, stride, layout, options.Binarization, xDpi, yDpi);
        return Jbig2Encoder.Encode(bitmap, options.Encoding);
    }
}
