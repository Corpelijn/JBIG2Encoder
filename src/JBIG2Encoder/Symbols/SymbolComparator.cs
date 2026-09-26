// Ported from jbig2enc (jbig2comparator.cc: jbig2enc_are_equivalent)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;

namespace JBIG2Encoder.Symbols;

/// <summary>
/// Decides whether two exemplars of identical size are "the same symbol" up to scanning noise. The bitmaps are
/// XORed and the difference is inspected for concentrated blobs: thin lines or larger clusters of differing pixels
/// indicate genuinely different shapes (e.g. a "c" and an "e"), scattered noise does not.
/// </summary>
internal static class SymbolComparator
{
    private const int Divider = 9;

    public static bool AreEquivalent(BinaryBitmap first, BinaryBitmap second)
    {
        int w = first.Width, h = first.Height;
        if (second.Width != w || second.Height != h) return false;

        var diff = first.Clone();
        diff.Combine(BinaryBitmap.RasterOp.Xor, 0, 0, w, h, second, 0, 0);

        // Shortcut to failure if the symbols are significantly different.
        int threshold = (int)(first.CountBlackPixels() * 0.25);
        if (diff.CountBlackPixels() > threshold) return false;

        const int vertical = Divider * 2;
        const int horizontal = Divider * 2;

        var parsed = new int[Divider, Divider];
        var horizontalParsed = new int[horizontal, Divider];
        var verticalParsed = new int[Divider, vertical];

        int verticalPart = h / Divider;
        int horizontalPart = w / Divider;

        int horizontalModuleCounter = 0;
        int verticalModuleCounter = 0;

        // The area of an ellipse, as a fraction of it is used as the "blob" threshold.
        int a, b;
        if (verticalPart < horizontalPart)
        {
            a = horizontalPart / 2;
            b = verticalPart / 2;
        }
        else
        {
            a = verticalPart / 2;
            b = horizontalPart / 2;
        }

        float pointThreshold = (float)(a * b * Math.PI);
        int verticalLineThreshold = (int)((verticalPart * (horizontalPart / 2)) * 0.9);
        int horizontalLineThreshold = (int)((horizontalPart * (verticalPart / 2)) * 0.9);

        // Count the differing pixels of the XOR image in a Divider x Divider grid of sub-matrices.
        for (int hp = 0; hp < Divider; hp++)
        {
            int horizontalStart = horizontalPart * hp + horizontalModuleCounter;
            int horizontalEnd;
            if (hp == Divider - 1)
            {
                horizontalModuleCounter = 0;
                horizontalEnd = w;
            }
            else if ((w - horizontalModuleCounter) % Divider > 0)
            {
                horizontalEnd = horizontalStart + horizontalPart + 1;
                horizontalModuleCounter++;
            }
            else
            {
                horizontalEnd = horizontalStart + horizontalPart;
            }

            for (int vp = 0; vp < Divider; vp++)
            {
                int verticalStart = verticalPart * vp + verticalModuleCounter;
                int verticalEnd;
                if (vp == Divider - 1)
                {
                    verticalModuleCounter = 0;
                    verticalEnd = h;
                }
                else if ((h - verticalModuleCounter) % Divider > 0)
                {
                    verticalEnd = verticalStart + verticalPart + 1;
                    verticalModuleCounter++;
                }
                else
                {
                    verticalEnd = verticalStart + verticalPart;
                }

                int left = 0, right = 0, up = 0, down = 0;
                int horizontalCenter = (horizontalStart + horizontalEnd) / 2;
                int verticalCenter = (verticalStart + verticalEnd) / 2;

                for (int i = horizontalStart; i < horizontalEnd; i++)
                {
                    for (int j = verticalStart; j < verticalEnd; j++)
                    {
                        if (!diff.GetPixel(i, j)) continue;

                        if (i < horizontalCenter) left++; else right++;
                        if (j < verticalCenter) up++; else down++;
                    }
                }

                parsed[hp, vp] = left + right;
                horizontalParsed[hp * 2, vp] = left;
                horizontalParsed[hp * 2 + 1, vp] = right;
                verticalParsed[hp, vp * 2] = up;
                verticalParsed[hp, vp * 2 + 1] = down;
            }
        }

        // Horizontal lines of differing pixels.
        for (int i = 0; i < horizontal - 1; i++)
        {
            for (int j = 0; j < Divider - 1; j++)
            {
                int sum = 0;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        sum += horizontalParsed[i + x, j + y];
                if (sum >= horizontalLineThreshold) return false;
            }
        }

        // Vertical lines.
        for (int i = 0; i < Divider - 1; i++)
        {
            for (int j = 0; j < vertical - 1; j++)
            {
                int sum = 0;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        sum += verticalParsed[i + x, j + y];
                if (sum >= verticalLineThreshold) return false;
            }
        }

        // Diagonal lines.
        for (int i = 0; i < Divider - 2; i++)
        {
            for (int j = 0; j < Divider - 2; j++)
            {
                int leftCross = 0, rightCross = 0;
                for (int x = 0; x < 3; x++)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        if (x == y) leftCross += parsed[i + x, j + y];
                        if (2 - x == y) rightCross += parsed[i + x, j + y];
                    }
                }
                if (leftCross >= horizontalLineThreshold || rightCross >= horizontalLineThreshold) return false;
            }
        }

        // Blobs: four neighboring sub-matrices holding more differing pixels than a fraction of an ellipse.
        for (int i = 0; i < Divider - 1; i++)
        {
            for (int j = 0; j < Divider - 1; j++)
            {
                int sum = 0;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        sum += parsed[i + x, j + y];
                if (sum >= pointThreshold) return false;
            }
        }

        return true;
    }
}
