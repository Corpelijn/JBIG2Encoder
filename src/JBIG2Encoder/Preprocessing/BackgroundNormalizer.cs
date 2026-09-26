// Re-implements the algorithm of Leptonica's pixCleanBackgroundToWhite / pixBackgroundNormSimple
// (adaptmap.c), which the jbig2enc command line tool uses before thresholding.
// Leptonica: Copyright (C) 2001-2020 Leptonica, BSD 2-clause. See the NOTICE file at the repository root.

using System;

namespace JBIG2Encoder.Preprocessing;

/// <summary>
/// Estimates the local paper/background level of a grayscale page in tiles and rescales the image so the
/// background becomes (near) white while ink stays dark. Removes gradients, shadows and paper tint that would
/// otherwise defeat a fixed threshold.
/// </summary>
internal static class BackgroundNormalizer
{
    // Defaults of pixBackgroundNormSimple().
    private const int TileWidth = 10;
    private const int TileHeight = 15;
    private const int ForegroundThreshold = 60;
    private const int MinCount = 40;
    private const int BackgroundValue = 200;
    private const int SmoothX = 2;
    private const int SmoothY = 1;

    // Foreground pixels are grown by this radius so that anti-aliased edges do not pollute the background estimate.
    private const int DilateRadius = 3;

    /// <summary>
    /// Returns a tightly packed (<paramref name="width"/> bytes per row) copy of the image with the background
    /// cleaned to white, using the same parameters as jbig2enc's default (local) mode.
    /// </summary>
    public static byte[] CleanToWhite(ReadOnlySpan<byte> gray, int width, int height, int stride, float gamma, int blackValue, int whiteValue)
    {
        if (whiteValue > 200) whiteValue = 190; // pixCleanBackgroundToWhite resets values above the background target

        byte[] result = Normalize(gray, width, height, stride) ?? Pack(gray, width, height, stride);
        ApplyGamma(result, gamma, blackValue, whiteValue);
        return result;
    }

    private static byte[] Pack(ReadOnlySpan<byte> gray, int width, int height, int stride)
    {
        var packed = new byte[width * height];
        for (int y = 0; y < height; y++) gray.Slice(y * stride, width).CopyTo(packed.AsSpan(y * width, width));
        return packed;
    }

    /// <summary>
    /// The background normalization proper (pixBackgroundNorm). Returns null when the image is too small to
    /// estimate a background map, in which case the caller uses the unmodified image.
    /// </summary>
    private static byte[]? Normalize(ReadOnlySpan<byte> gray, int w, int h, int stride)
    {
        int nx = w / TileWidth;   // number of complete tiles
        int ny = h / TileHeight;
        int mapW = (w + TileWidth - 1) / TileWidth;
        int mapH = (h + TileHeight - 1) / TileHeight;
        if (nx == 0 || ny == 0 || mapW < 5 || mapH < 5) return null;

        byte[] map = MeasureBackground(gray, w, h, stride, nx, ny, mapW, mapH);
        if (!FillMapHoles(map, mapW, mapH, nx, ny)) return null;

        ushort[] inverse = InvertAndSmooth(map, mapW, mapH);

        var result = new byte[w * h];
        for (int ty = 0; ty < mapH; ty++)
        {
            int y0 = ty * TileHeight;
            int y1 = Math.Min(y0 + TileHeight, h);
            for (int tx = 0; tx < mapW; tx++)
            {
                int x0 = tx * TileWidth;
                int x1 = Math.Min(x0 + TileWidth, w);
                int factor = inverse[ty * mapW + tx];
                for (int y = y0; y < y1; y++)
                {
                    ReadOnlySpan<byte> src = gray.Slice(y * stride, w);
                    Span<byte> dst = result.AsSpan(y * w, w);
                    for (int x = x0; x < x1; x++)
                    {
                        int v = (src[x] * factor) >> 8;
                        dst[x] = (byte)(v > 255 ? 255 : v);
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// For every complete tile: the mean of the pixels that are not near dark (foreground) pixels, or 0 (a hole)
    /// when fewer than <see cref="MinCount"/> such pixels exist (pixGetBackgroundGrayMap).
    /// </summary>
    private static byte[] MeasureBackground(ReadOnlySpan<byte> gray, int w, int h, int stride, int nx, int ny, int mapW, int mapH)
    {
        var map = new byte[mapW * mapH];

        // Horizontally dilated foreground for the rows needed by one tile row: rows [y0 - r, y0 + TileHeight + r).
        int band = TileHeight + 2 * DilateRadius;
        var hrows = new byte[band][];
        for (int i = 0; i < band; i++) hrows[i] = new byte[w];
        var prefix = new int[w + 1];
        var vmask = new byte[w];

        for (int ty = 0; ty < ny; ty++)
        {
            int y0 = ty * TileHeight;

            for (int k = 0; k < band; k++)
            {
                int y = y0 - DilateRadius + k;
                byte[] hrow = hrows[k];
                if (y < 0 || y >= h)
                {
                    Array.Clear(hrow);
                    continue;
                }

                ReadOnlySpan<byte> src = gray.Slice(y * stride, w);
                prefix[0] = 0;
                for (int x = 0; x < w; x++) prefix[x + 1] = prefix[x] + (src[x] < ForegroundThreshold ? 1 : 0);
                for (int x = 0; x < w; x++)
                {
                    int lo = Math.Max(0, x - DilateRadius);
                    int hi = Math.Min(w, x + DilateRadius + 1);
                    hrow[x] = prefix[hi] - prefix[lo] > 0 ? (byte)1 : (byte)0;
                }
            }

            var sum = new int[nx];
            var count = new int[nx];
            for (int r = 0; r < TileHeight; r++)
            {
                // Vertical part of the dilation: OR of the 2 * radius + 1 rows around row r.
                Array.Clear(vmask);
                for (int d = 0; d <= 2 * DilateRadius; d++)
                {
                    byte[] hrow = hrows[r + d];
                    for (int x = 0; x < w; x++) vmask[x] |= hrow[x];
                }

                ReadOnlySpan<byte> src = gray.Slice((y0 + r) * stride, w);
                for (int tx = 0; tx < nx; tx++)
                {
                    int xs = tx * TileWidth;
                    int s = 0, c = 0;
                    for (int m = 0; m < TileWidth; m++)
                    {
                        if (vmask[xs + m] == 0)
                        {
                            s += src[xs + m];
                            c++;
                        }
                    }
                    sum[tx] += s;
                    count[tx] += c;
                }
            }

            for (int tx = 0; tx < nx; tx++)
            {
                if (count[tx] >= MinCount) map[ty * mapW + tx] = (byte)(sum[tx] / count[tx]);
            }
        }
        return map;
    }

    /// <summary>
    /// Fills map entries that are 0 (no background measured) from their neighbors: downwards/upwards within a
    /// column, then whole columns from adjacent columns (pixFillMapHoles with L_FILL_BLACK).
    /// </summary>
    /// <returns>False when no column contains any measurement.</returns>
    private static bool FillMapHoles(byte[] map, int w, int h, int nx, int ny)
    {
        var columnHasData = new bool[nx];
        int missing = 0;

        for (int j = 0; j < nx; j++)
        {
            int found = -1;
            for (int i = 0; i < ny; i++)
            {
                if (map[i * w + j] != 0)
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                missing++;
                continue;
            }

            columnHasData[j] = true;
            byte value = map[found * w + j];
            for (int i = found - 1; i >= 0; i--) map[i * w + j] = value; // replicate upwards

            byte last = map[j];
            for (int i = 1; i < h; i++)
            {
                byte v = map[i * w + j];
                if (v == 0) map[i * w + j] = last; else last = v;
            }
        }

        if (missing == nx) return false;

        if (missing > 0)
        {
            int good = 0;
            while (!columnHasData[good]) good++;

            for (int j = good - 1; j >= 0; j--) CopyColumn(map, w, h, j + 1, j);
            for (int j = good + 1; j < nx; j++)
            {
                if (!columnHasData[j]) CopyColumn(map, w, h, j - 1, j);
            }
        }

        if (w > nx) CopyColumn(map, w, h, w - 2, w - 1); // the partial right-most tile column
        return true;
    }

    private static void CopyColumn(byte[] map, int w, int h, int from, int to)
    {
        for (int i = 0; i < h; i++) map[i * w + to] = map[i * w + from];
    }

    /// <summary>
    /// Smooths the map with a (2*SmoothX+1) x (2*SmoothY+1) box mean and converts it to a multiplier
    /// <c>256 * background / value</c> (pixGetInvBackgroundMap).
    /// </summary>
    private static ushort[] InvertAndSmooth(byte[] map, int w, int h)
    {
        // Summed-area table for the box means.
        var integral = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += map[y * w + x];
                integral[(y + 1) * (w + 1) + (x + 1)] = integral[y * (w + 1) + (x + 1)] + rowSum;
            }
        }

        var inverse = new ushort[w * h];
        for (int y = 0; y < h; y++)
        {
            int ya = Math.Max(0, y - SmoothY), yb = Math.Min(h, y + SmoothY + 1);
            for (int x = 0; x < w; x++)
            {
                int xa = Math.Max(0, x - SmoothX), xb = Math.Min(w, x + SmoothX + 1);
                int total = integral[yb * (w + 1) + xb] - integral[ya * (w + 1) + xb]
                          - integral[yb * (w + 1) + xa] + integral[ya * (w + 1) + xa];
                int cells = (xb - xa) * (yb - ya);
                int smoothed = (total + cells / 2) / cells;
                inverse[y * w + x] = (ushort)(smoothed > 0 ? (256 * BackgroundValue) / smoothed : BackgroundValue / 2);
            }
        }
        return inverse;
    }

    /// <summary>Maps <paramref name="blackValue"/> and below to 0 and <paramref name="whiteValue"/> and above to 255 (numaGammaTRC).</summary>
    private static void ApplyGamma(byte[] pixels, float gamma, int blackValue, int whiteValue)
    {
        if (gamma <= 0f) gamma = 1f;
        float invGamma = 1f / gamma;

        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            if (i < blackValue) table[i] = 0;
            else if (i > whiteValue) table[i] = 255;
            else
            {
                float x = (float)(i - blackValue) / (whiteValue - blackValue);
                int v = (int)(255.0 * MathF.Pow(x, invGamma) + 0.5);
                table[i] = (byte)Math.Clamp(v, 0, 255);
            }
        }

        for (int i = 0; i < pixels.Length; i++) pixels[i] = table[pixels[i]];
    }
}
