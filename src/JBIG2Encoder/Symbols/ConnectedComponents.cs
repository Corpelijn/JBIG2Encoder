// Re-implements the behavior of Leptonica's pixConnComp (conncomp.c) used by jbig2enc:
// components are found in raster order of their first pixel, and each component bitmap contains only the
// pixels of that component. Leptonica: Copyright (C) 2001-2020 Leptonica, BSD 2-clause (see NOTICE).

using System;
using System.Collections.Generic;
using System.Numerics;

namespace JBIG2Encoder.Symbols;

/// <summary>A connected component: its tight bitmap and the position of its upper-left corner on the page.</summary>
internal sealed class Component
{
    public Component(BinaryBitmap bitmap, int x, int y)
    {
        Bitmap = bitmap;
        X = x;
        Y = y;
    }

    /// <summary>Only the pixels of this component, cropped to its bounding box.</summary>
    public BinaryBitmap Bitmap { get; }

    /// <summary>Column of the bounding box's left edge on the page.</summary>
    public int X { get; }

    /// <summary>Row of the bounding box's top edge on the page.</summary>
    public int Y { get; }
}

internal static class ConnectedComponents
{
    /// <summary>
    /// Extracts the 8-connected components of a page. They are returned in raster order of their first
    /// (top-most, then left-most) pixel.
    /// </summary>
    public static List<Component> Extract(BinaryBitmap page)
    {
        var result = new List<Component>();
        Run(page, eight: true, result);
        return result;
    }

    /// <summary>Counts connected components.</summary>
    public static int Count(BinaryBitmap bitmap, bool eightConnected) => Run(bitmap, eightConnected, null);


    private static int Run(BinaryBitmap source, bool eight, List<Component>? output)
    {
        BinaryBitmap work = source.Clone();
        int w = work.Width;
        int h = work.Height;
        int wpr = work.WordsPerRow;
        Span<uint> words = work.Words;

        var stack = new List<(int X, int Y)>();
        var runs = new List<(int Y, int Left, int Right)>();

        int count = 0;
        int startX = 0, startY = 0;
        while (NextOnPixel(words, w, h, wpr, ref startX, ref startY))
        {
            int minX = w, maxX = -1, minY = h, maxY = -1;
            runs.Clear();
            stack.Clear();
            stack.Add((startX, startY));

            while (stack.Count > 0)
            {
                (int x, int y) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (!IsSet(words, wpr, x, y)) continue; // already claimed through another seed

                int left = x;
                while (left > 0 && IsSet(words, wpr, left - 1, y)) left--;
                int right = x;
                while (right < w - 1 && IsSet(words, wpr, right + 1, y)) right++;
                ClearRun(words, wpr, y, left, right);

                if (left < minX) minX = left;
                if (right > maxX) maxX = right;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (output != null) runs.Add((y, left, right));

                int lo = eight ? Math.Max(0, left - 1) : left;
                int hi = eight ? Math.Min(w - 1, right + 1) : right;
                for (int ny = y - 1; ny <= y + 1; ny += 2)
                {
                    if (ny < 0 || ny >= h) continue;
                    bool inRun = false;
                    for (int nx = lo; nx <= hi; nx++)
                    {
                        bool on = IsSet(words, wpr, nx, ny);
                        if (on && !inRun) stack.Add((nx, ny));
                        inRun = on;
                    }
                }
            }

            count++;
            if (output != null)
            {
                var bmp = new BinaryBitmap(maxX - minX + 1, maxY - minY + 1);
                foreach (var (ry, left, right) in runs) SetRun(bmp, ry - minY, left - minX, right - minX);
                output.Add(new Component(bmp, minX, minY));
            }
        }
        return count;
    }

    private static bool IsSet(ReadOnlySpan<uint> words, int wpr, int x, int y) =>
        (words[y * wpr + (x >> 5)] & (0x80000000u >> (x & 31))) != 0;

    /// <summary>Finds the next set pixel in raster order at or after (x, y).</summary>
    private static bool NextOnPixel(ReadOnlySpan<uint> words, int w, int h, int wpr, ref int x, ref int y)
    {
        int startX = x;
        for (int row = y; row < h; row++)
        {
            int wi = row == y ? startX >> 5 : 0;
            for (; wi < wpr; wi++)
            {
                uint word = words[row * wpr + wi];
                if (row == y && wi == startX >> 5 && (startX & 31) != 0)
                {
                    word &= uint.MaxValue >> (startX & 31);
                }
                if (word == 0) continue;

                x = wi * 32 + BitOperations.LeadingZeroCount(word);
                y = row;
                return true;
            }
        }
        return false;
    }

    private static uint RunMask(int firstBit, int lastBit) =>
        (uint.MaxValue >> firstBit) & (uint.MaxValue << (31 - lastBit));

    private static void ClearRun(Span<uint> words, int wpr, int y, int left, int right)
    {
        int firstWord = left >> 5, lastWord = right >> 5;
        for (int wi = firstWord; wi <= lastWord; wi++)
        {
            int a = wi == firstWord ? left & 31 : 0;
            int b = wi == lastWord ? right & 31 : 31;
            words[y * wpr + wi] &= ~RunMask(a, b);
        }
    }

    private static void SetRun(BinaryBitmap bmp, int y, int left, int right)
    {
        Span<uint> row = bmp.Row(y);
        int firstWord = left >> 5, lastWord = right >> 5;
        for (int wi = firstWord; wi <= lastWord; wi++)
        {
            int a = wi == firstWord ? left & 31 : 0;
            int b = wi == lastWord ? right & 31 : 31;
            row[wi] |= RunMask(a, b);
        }
    }
}
