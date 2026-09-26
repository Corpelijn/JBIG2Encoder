using System;

namespace JBIG2Encoder.Tests.TestSupport;

/// <summary>Deterministic synthetic bi-level test images.</summary>
internal static class TestImages
{
    public static BinaryBitmap Blank(int w, int h) => new(w, h);

    public static BinaryBitmap Solid(int w, int h)
    {
        var bmp = new BinaryBitmap(w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) bmp.SetPixel(x, y, true);
        return bmp;
    }

    /// <summary>Uniform random noise; <paramref name="density"/> is the probability of a black pixel.</summary>
    public static BinaryBitmap Noise(int w, int h, double density, int seed)
    {
        var rng = new Random(seed);
        var bmp = new BinaryBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.NextDouble() < density) bmp.SetPixel(x, y, true);
        return bmp;
    }

    /// <summary>Lines, boxes and diagonals; exercises long runs and duplicate rows.</summary>
    public static BinaryBitmap Geometry(int w, int h)
    {
        var bmp = new BinaryBitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool on =
                    x == y ||
                    x == w - 1 - y ||
                    (y % 11) == 3 ||
                    (x % 17) == 5 ||
                    (x > w / 4 && x < w / 2 && y > h / 4 && y < h / 2);
                if (on) bmp.SetPixel(x, y, true);
            }
        }
        return bmp;
    }

    // A tiny 5x7 bitmap font for a handful of glyphs. Rows are 5 bits, MSB = leftmost.
    private static readonly byte[][] Glyphs =
    {
        new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 }, // A
        new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E }, // B
        new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E }, // C
        new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E }, // D
        new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F }, // E
        new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, // 0
        new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, // 2
        new byte[] { 0x0E, 0x11, 0x10, 0x1E, 0x11, 0x11, 0x0E }, // 6
        new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, // 8
        new byte[] { 0x04, 0x0C, 0x14, 0x04, 0x04, 0x04, 0x1F }, // 1
    };

    /// <summary>
    /// A page of "text": rows of scaled glyphs on a grid with optional speckle noise. Repeated glyphs make it a
    /// realistic input for the symbol coder.
    /// </summary>
    /// <param name="scale">Pixel scale of each glyph cell (glyphs are 5x7 cells).</param>
    /// <param name="noise">Probability of flipping an individual pixel within glyph bounding areas (0 = crisp).</param>
    public static BinaryBitmap TextPage(int w, int h, int scale, int seed, double noise = 0.0)
    {
        var rng = new Random(seed);
        var bmp = new BinaryBitmap(w, h);
        int cellW = 6 * scale;
        int cellH = 9 * scale;
        for (int gy = 4; gy + 7 * scale < h; gy += cellH)
        {
            for (int gx = 4; gx + 5 * scale < w; gx += cellW)
            {
                if (rng.NextDouble() < 0.15) continue; // gaps between words
                byte[] glyph = Glyphs[rng.Next(Glyphs.Length)];
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if ((glyph[row] & (0x10 >> col)) == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                bmp.SetPixel(gx + col * scale + dx, gy + row * scale + dy, true);
                    }
                }
            }
        }

        if (noise > 0)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (rng.NextDouble() < noise) bmp.SetPixel(x, y, !bmp.GetPixel(x, y));
        }
        return bmp;
    }
}
