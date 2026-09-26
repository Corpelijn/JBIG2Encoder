using System;
using System.Numerics;

namespace JBIG2Encoder;

/// <summary>
/// A 1 bit-per-pixel image. <c>true</c> means a black (foreground / "ink") pixel, which is
/// the polarity JBIG2 uses (1 = black).
/// </summary>
/// <remarks>
/// Storage layout is 32-bit words per row with the most significant bit holding the leftmost
/// pixel, and all padding bits at the end of each row always zero. This is the same layout
/// Leptonica uses, which the encoder algorithms (ported from jbig2enc) rely on.
/// </remarks>
public sealed class BinaryBitmap
{
    private readonly uint[] _words;

    /// <summary>Creates an all-white bitmap.</summary>
    public BinaryBitmap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        long words = (long)((width + 31) >> 5) * height;
        if (words > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(width), "Bitmap is too large.");

        Width = width;
        Height = height;
        WordsPerRow = (width + 31) >> 5;
        _words = new uint[(int)words];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Horizontal resolution in dots per inch, or 0 when unknown.</summary>
    public int XResolutionDpi { get; set; }

    /// <summary>Vertical resolution in dots per inch, or 0 when unknown.</summary>
    public int YResolutionDpi { get; set; }

    internal int WordsPerRow { get; }

    internal Span<uint> Words => _words;

    internal Span<uint> Row(int y) => _words.AsSpan(y * WordsPerRow, WordsPerRow);

    /// <summary>Mask of the valid (non-padding) bits in the last word of every row.</summary>
    internal uint LastWordMask => (Width & 31) == 0 ? uint.MaxValue : uint.MaxValue << (32 - (Width & 31));

    /// <summary>Returns whether the pixel is black. Coordinates outside the bitmap are white.</summary>
    public bool GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        return (_words[y * WordsPerRow + (x >> 5)] & (0x80000000u >> (x & 31))) != 0;
    }

    /// <summary>Sets a pixel. Coordinates outside the bitmap are ignored.</summary>
    public void SetPixel(int x, int y, bool black)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        ref uint w = ref _words[y * WordsPerRow + (x >> 5)];
        uint bit = 0x80000000u >> (x & 31);
        if (black) w |= bit; else w &= ~bit;
    }

    /// <summary>Counts the black pixels.</summary>
    public int CountBlackPixels()
    {
        int count = 0;
        foreach (uint w in _words) count += BitOperations.PopCount(w);
        return count;
    }

    /// <summary>Returns true when no pixel is black.</summary>
    public bool IsBlank()
    {
        foreach (uint w in _words) if (w != 0) return false;
        return true;
    }

    /// <summary>Creates an independent copy (including the resolution metadata).</summary>
    public BinaryBitmap Clone()
    {
        var copy = new BinaryBitmap(Width, Height) { XResolutionDpi = XResolutionDpi, YResolutionDpi = YResolutionDpi };
        _words.CopyTo(copy._words, 0);
        return copy;
    }

    /// <summary>Compares pixel content (ignores resolution metadata).</summary>
    public bool PixelsEqual(BinaryBitmap other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Width == other.Width && Height == other.Height && _words.AsSpan().SequenceEqual(other._words);
    }

    #region Import / export

    /// <summary>
    /// Creates a bitmap from rows of packed bits (most significant bit = leftmost pixel).
    /// </summary>
    /// <param name="data">Packed pixel data.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes per row in <paramref name="data"/> (at least (width + 7) / 8).</param>
    /// <param name="oneIsBlack">True when a set bit means black (JBIG2 / most 1bpp masks); false when a set bit means white (PDF DeviceGray, PBM-inverse, Windows BMP palette 0=black).</param>
    public static BinaryBitmap FromPackedBits(ReadOnlySpan<byte> data, int width, int height, int stride, bool oneIsBlack)
    {
        int rowBytes = (width + 7) >> 3;
        if (stride < rowBytes) throw new ArgumentOutOfRangeException(nameof(stride));
        if (data.Length < (long)stride * (height - 1) + rowBytes) throw new ArgumentException("Data is too short for the given dimensions.", nameof(data));

        var bmp = new BinaryBitmap(width, height);
        uint lastMask = bmp.LastWordMask;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> src = data.Slice(y * stride, rowBytes);
            Span<uint> row = bmp.Row(y);
            for (int i = 0; i < row.Length; i++)
            {
                uint w = 0;
                int b = i * 4;
                for (int k = 0; k < 4; k++)
                {
                    w <<= 8;
                    if (b + k < rowBytes) w |= src[b + k];
                }
                row[i] = oneIsBlack ? w : ~w;
            }
            row[^1] &= lastMask;
        }
        return bmp;
    }

    /// <summary>
    /// Creates a bitmap from an 8-bit grayscale image with a fixed threshold: pixels darker than
    /// <paramref name="threshold"/> (value &lt; threshold) become black.
    /// </summary>
    public static BinaryBitmap FromGray8(ReadOnlySpan<byte> pixels, int width, int height, int stride, byte threshold)
    {
        if (stride < width) throw new ArgumentOutOfRangeException(nameof(stride));
        if (pixels.Length < (long)stride * (height - 1) + width) throw new ArgumentException("Data is too short for the given dimensions.", nameof(pixels));

        var bmp = new BinaryBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> src = pixels.Slice(y * stride, width);
            Span<uint> row = bmp.Row(y);
            for (int x = 0; x < width; x++)
            {
                if (src[x] < threshold) row[x >> 5] |= 0x80000000u >> (x & 31);
            }
        }
        return bmp;
    }

    /// <summary>Exports the bitmap as packed rows (most significant bit = leftmost pixel).</summary>
    /// <param name="oneIsBlack">True when a set bit should mean black.</param>
    /// <param name="stride">Receives the number of bytes per row.</param>
    public byte[] ToPackedBits(bool oneIsBlack, out int stride)
    {
        stride = (Width + 7) >> 3;
        var result = new byte[stride * Height];
        for (int y = 0; y < Height; y++)
        {
            ReadOnlySpan<uint> row = Row(y);
            for (int i = 0; i < stride; i++)
            {
                byte b = (byte)(row[i >> 2] >> (24 - 8 * (i & 3)));
                result[y * stride + i] = oneIsBlack ? b : (byte)~b;
            }
            if (!oneIsBlack && (Width & 7) != 0)
            {
                // Padding bits at the end of each row are white in the inverted polarity as well.
                result[y * stride + stride - 1] |= (byte)(0xFF >> (Width & 7));
            }
        }
        return result;
    }

    #endregion

    #region Internal raster operations

    internal enum RasterOp { Copy, Or, And, Xor, AndNot }

    /// <summary>
    /// Returns 32 pixels starting at bit position <paramref name="bitPos"/> of a row
    /// (position 0 = leftmost pixel). Positions outside the row read as white.
    /// </summary>
    internal static uint Get32(ReadOnlySpan<uint> row, int bitPos)
    {
        int wi = bitPos >> 5;
        int sh = bitPos & 31;
        uint a = (uint)wi < (uint)row.Length ? row[wi] : 0u;
        if (sh == 0) return a;
        uint b = (uint)(wi + 1) < (uint)row.Length ? row[wi + 1] : 0u;
        return (a << sh) | (b >> (32 - sh));
    }

    /// <summary>
    /// Combines a rectangle of <paramref name="src"/> into this bitmap. The operation is clipped to the
    /// bounds of both bitmaps, like a classic raster-op.
    /// </summary>
    internal void Combine(RasterOp op, int dstX, int dstY, int w, int h, BinaryBitmap src, int srcX, int srcY)
    {
        // Clip against the destination.
        if (dstX < 0) { w += dstX; srcX -= dstX; dstX = 0; }
        if (dstY < 0) { h += dstY; srcY -= dstY; dstY = 0; }
        if (dstX + w > Width) w = Width - dstX;
        if (dstY + h > Height) h = Height - dstY;
        // Clip against the source.
        if (srcX < 0) { w += srcX; dstX -= srcX; srcX = 0; }
        if (srcY < 0) { h += srcY; dstY -= srcY; srcY = 0; }
        if (srcX + w > src.Width) w = src.Width - srcX;
        if (srcY + h > src.Height) h = src.Height - srcY;
        if (w <= 0 || h <= 0) return;

        int x0 = dstX;
        int x1 = dstX + w; // exclusive
        int firstWord = x0 >> 5;
        int lastWord = (x1 - 1) >> 5;
        int shift = srcX - dstX; // dst bit position p reads source bit position p + shift

        for (int r = 0; r < h; r++)
        {
            Span<uint> drow = Row(dstY + r);
            ReadOnlySpan<uint> srow = src.Row(srcY + r);
            for (int wi = firstWord; wi <= lastWord; wi++)
            {
                uint mask = uint.MaxValue;
                if (wi == firstWord) mask &= uint.MaxValue >> (x0 & 31);
                if (wi == lastWord && ((x1 & 31) != 0)) mask &= uint.MaxValue << (32 - (x1 & 31));

                uint s = Get32(srow, wi * 32 + shift);
                uint d = drow[wi];
                uint n = op switch
                {
                    RasterOp.Copy => s,
                    RasterOp.Or => d | s,
                    RasterOp.And => d & s,
                    RasterOp.Xor => d ^ s,
                    RasterOp.AndNot => d & ~s,
                    _ => d,
                };
                drow[wi] = (d & ~mask) | (n & mask);
            }
        }
    }

    /// <summary>Copies a rectangle out as a new bitmap; areas outside this bitmap are white.</summary>
    internal BinaryBitmap Clip(int x, int y, int w, int h)
    {
        var result = new BinaryBitmap(w, h);
        result.Combine(RasterOp.Copy, 0, 0, w, h, this, x, y);
        return result;
    }

    /// <summary>Returns a copy with <paramref name="border"/> white pixels added on every side.</summary>
    internal BinaryBitmap AddBorder(int border)
    {
        var result = new BinaryBitmap(Width + 2 * border, Height + 2 * border);
        result.Combine(RasterOp.Copy, border, border, Width, Height, this, 0, 0);
        return result;
    }

    /// <summary>Returns a copy with <paramref name="border"/> pixels removed on every side.</summary>
    internal BinaryBitmap RemoveBorder(int border) => Clip(border, border, Width - 2 * border, Height - 2 * border);

    #endregion
}
