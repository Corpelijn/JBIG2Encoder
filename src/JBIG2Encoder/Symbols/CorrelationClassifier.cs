// Re-implements Leptonica's windowed-correlation JBIG2 classifier (jbclass.c: jbClassifyCorrelation,
// jbGetULCorners, jbGetLLCorners, finalPositioningForAlignment; correlscore.c: pixCorrelationScoreThresholded)
// in the configuration jbig2enc uses it. Leptonica: Copyright (C) 2001-2020 Leptonica, BSD 2-clause (see NOTICE).

using System;
using System.Collections.Generic;
using System.Numerics;

namespace JBIG2Encoder.Symbols;

/// <summary>A class exemplar ("symbol"): the first instance seen of a class, with a white border.</summary>
internal sealed class SymbolTemplate
{
    public SymbolTemplate(BinaryBitmap bordered, int foregroundArea, float centroidX, float centroidY)
    {
        Bordered = bordered;
        Width = bordered.Width - 2 * CorrelationClassifier.Border;
        Height = bordered.Height - 2 * CorrelationClassifier.Border;
        ForegroundArea = foregroundArea;
        CentroidX = centroidX;
        CentroidY = centroidY;
    }

    /// <summary>The exemplar surrounded by <see cref="CorrelationClassifier.Border"/> white pixels on every side.</summary>
    public BinaryBitmap Bordered { get; }

    /// <summary>Width without the border.</summary>
    public int Width { get; }

    /// <summary>Height without the border.</summary>
    public int Height { get; }

    /// <summary>Number of black pixels.</summary>
    public int ForegroundArea { get; }

    /// <summary>Centroid relative to the upper-left corner of <see cref="Bordered"/>.</summary>
    public float CentroidX { get; }

    public float CentroidY { get; }

    /// <summary>Width * height without the border.</summary>
    public int BoxArea => Width * Height;

    /// <summary>The exemplar cropped to its bounding box, i.e. as it is stored in a symbol dictionary.</summary>
    public BinaryBitmap ToUnbordered() => Bordered.RemoveBorder(CorrelationClassifier.Border);
}

/// <summary>
/// Groups the connected components of all pages into classes of similar shapes. The first instance of each
/// class becomes its exemplar; later instances that correlate well enough with an exemplar are replaced by it.
/// </summary>
internal sealed class CorrelationClassifier
{
    /// <summary>White pixels added around every component while classifying (JB_ADDED_PIXELS).</summary>
    public const int Border = 6;

    private const int MaxDiffWidth = 2;
    private const int MaxDiffHeight = 2;

    // Template sizes searched for a match: the same size first, then neighbors within +/-2, in a spiral
    // (two_by_two_walk in jbclass.c).
    private static readonly (int Dx, int Dy)[] SizeWalk =
    {
        (0, 0), (0, 1), (-1, 0), (0, -1), (1, 0), (-1, 1), (1, 1), (-1, -1), (1, -1),
        (0, -2), (2, 0), (0, 2), (-2, 0), (-1, -2), (1, -2), (2, -1), (2, 1), (1, 2), (-1, 2),
        (-2, 1), (-2, -1), (-2, -2), (2, -2), (2, 2), (-2, 2),
    };

    private static readonly int[] SumTable = BuildSumTable();
    private static readonly int[] CentroidTable = BuildCentroidTable();

    private readonly float _threshold;
    private readonly float _weight;
    private readonly Dictionary<long, List<int>> _templatesBySize = new();
    private readonly List<float> _componentCentroidX = new();
    private readonly List<float> _componentCentroidY = new();
    private bool _finished;

    public CorrelationClassifier(float threshold, float weight)
    {
        _threshold = threshold;
        _weight = weight;
    }

    /// <summary>Class exemplars, indexed by class number.</summary>
    public List<SymbolTemplate> Templates { get; } = new();

    /// <summary>Class of every component of every page, in the order the components were found.</summary>
    public List<int> ComponentClass { get; } = new();

    /// <summary>Page (0-based) of every component.</summary>
    public List<int> ComponentPage { get; } = new();

    /// <summary>
    /// For every component, the upper-left corner at which its class exemplar has to be placed so that the
    /// exemplar's centroid lands on the component's centroid (ptaul).
    /// </summary>
    public List<(int X, int Y)> ComponentUpperLeft { get; } = new();

    /// <summary>Index of the first component of every page (baseindex).</summary>
    public List<int> PageFirstComponent { get; } = new();

    public int PageCount => PageFirstComponent.Count;

    /// <summary>Marks classification as complete; templates may then be merged and no page can be added.</summary>
    public void Finish() => _finished = true;

    /// <summary>Extracts and classifies the components of a page.</summary>
    public void AddPage(BinaryBitmap page)
    {
        if (_finished) throw new InvalidOperationException("Pages cannot be added after classification has been finished.");

        int firstComponent = ComponentClass.Count;
        int pageIndex = PageFirstComponent.Count;
        PageFirstComponent.Add(firstComponent);

        List<Component> components = ConnectedComponents.Extract(page);
        int n = components.Count;
        if (n == 0) return;

        // Every component with a border, its foreground count, centroid and the per-row counts used for early exit.
        var bordered = new BinaryBitmap[n];
        var areas = new int[n];
        var downCounts = new int[n][];
        for (int i = 0; i < n; i++)
        {
            bordered[i] = components[i].Bitmap.AddBorder(Border);
            Measure(bordered[i], out areas[i], out float cx, out float cy, out downCounts[i]);
            _componentCentroidX.Add(cx);
            _componentCentroidY.Add(cy);
        }

        for (int i = 0; i < n; i++)
        {
            int index = firstComponent + i;
            int match = FindMatch(bordered[i], areas[i], _componentCentroidX[index], _componentCentroidY[index], downCounts[i]);
            if (match < 0)
            {
                match = Templates.Count;
                Templates.Add(new SymbolTemplate(bordered[i], areas[i], _componentCentroidX[index], _componentCentroidY[index]));
                long key = SizeKey(Templates[match].Width, Templates[match].Height);
                if (!_templatesBySize.TryGetValue(key, out List<int>? bucket)) _templatesBySize[key] = bucket = new List<int>();
                bucket.Add(match);
            }

            ComponentClass.Add(match);
            ComponentPage.Add(pageIndex);
        }

        // Where to place each exemplar on the page (jbGetULCorners).
        for (int i = 0; i < n; i++)
        {
            int index = firstComponent + i;
            SymbolTemplate template = Templates[ComponentClass[index]];
            float delx = template.CentroidX - _componentCentroidX[index];
            float dely = template.CentroidY - _componentCentroidY[index];
            int idelx = RoundAwayFromZero(delx);
            int idely = RoundAwayFromZero(dely);

            FinalPositioning(page, components[i].X, components[i].Y, idelx, idely, template.Bordered, out int dx, out int dy);
            ComponentUpperLeft.Add((components[i].X - idelx + dx, components[i].Y - idely + dy));
        }
    }

    /// <summary>
    /// The lower-left corner of every component's exemplar (jbGetLLCorners): the coordinates a JBIG2 text region
    /// uses with a bottom-left reference corner.
    /// </summary>
    public (int X, int Y)[] GetLowerLeftCorners()
    {
        var result = new (int X, int Y)[ComponentUpperLeft.Count];
        for (int i = 0; i < result.Length; i++)
        {
            (int x, int y) = ComponentUpperLeft[i];
            result[i] = (x, y + Templates[ComponentClass[i]].Height - 1);
        }
        return result;
    }

    private static long SizeKey(int width, int height) => ((long)width << 32) | (uint)height;

    private static int RoundAwayFromZero(float v) => v >= 0 ? (int)(v + 0.5f) : (int)(v - 0.5f);

    /// <summary>Returns the first exemplar (in search order) the instance correlates with, or -1.</summary>
    private int FindMatch(BinaryBitmap instance, int area1, float x1, float y1, int[] downCount)
    {
        int w = instance.Width - 2 * Border;
        int h = instance.Height - 2 * Border;

        foreach ((int dx, int dy) in SizeWalk)
        {
            int desiredW = w + dx, desiredH = h + dy;
            if (desiredW < 1 || desiredH < 1) continue;
            if (!_templatesBySize.TryGetValue(SizeKey(desiredW, desiredH), out List<int>? bucket)) continue;

            foreach (int index in bucket)
            {
                SymbolTemplate template = Templates[index];

                // Raise the threshold for "heavy" (mostly black) templates, which correlate well with many shapes.
                float threshold = _threshold;
                if (_weight > 0f)
                {
                    threshold = (float)(_threshold + (1.0 - _threshold) * _weight * template.ForegroundArea / template.BoxArea);
                }

                if (IsCorrelated(instance, template.Bordered, area1, template.ForegroundArea,
                        x1 - template.CentroidX, y1 - template.CentroidY, downCount, threshold))
                {
                    return index;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// True when correlation(instance, template) = |AND|^2 / (|instance| * |template|) is at least the threshold,
    /// after aligning the two by the (rounded) centroid difference. Aborts early when the outcome is decided.
    /// </summary>
    private static bool IsCorrelated(BinaryBitmap instance, BinaryBitmap template, int area1, int area2,
        float delx, float dely, int[] downCount, float scoreThreshold)
    {
        int wi = instance.Width, hi = instance.Height;
        int wt = template.Width, ht = template.Height;
        if (Math.Abs(wi - wt) > MaxDiffWidth || Math.Abs(hi - ht) > MaxDiffHeight) return false;

        int idelx = RoundAwayFromZero(delx);
        int idely = RoundAwayFromZero(dely);

        // The number of overlapping black pixels needed so that count^2 / (area1 * area2) >= threshold.
        int needed = (int)Math.Ceiling(Math.Sqrt((double)scoreThreshold * area1 * area2));

        // Only rows and columns where the shifted template lies over the instance can contribute.
        int loRow = Math.Max(idely, 0);
        int hiRow = Math.Min(ht + idely, hi);
        int loCol = Math.Max(idelx, 0);
        int hiCol = Math.Min(wt + idelx, wi);
        if (loCol >= hiCol || loRow >= hiRow) return false;

        // Black pixels of the instance in rows the template cannot reach.
        int unreachable = downCount[hiRow - 1];

        int firstWord = loCol >> 5;
        int lastWord = (hiCol - 1) >> 5;
        int count = 0;
        for (int y = loRow; y < hiRow; y++)
        {
            ReadOnlySpan<uint> row1 = instance.Row(y);
            ReadOnlySpan<uint> row2 = template.Row(y - idely);
            for (int wIndex = firstWord; wIndex <= lastWord; wIndex++)
            {
                count += BitOperations.PopCount(row1[wIndex] & BinaryBitmap.Get32(row2, wIndex * 32 - idelx));
            }

            if (count >= needed) return true;
            if (count + downCount[y] - unreachable < needed) return false;
        }
        return false;
    }

    /// <summary>
    /// Foreground pixel count, centroid and, for each row, the number of black pixels below it. The floating-point
    /// accumulation mirrors Leptonica's (single precision, same order) so class assignments match.
    /// </summary>
    private static void Measure(BinaryBitmap bitmap, out int area, out float centroidX, out float centroidY, out int[] downCounts)
    {
        int h = bitmap.Height;
        int wpl = bitmap.WordsPerRow;
        downCounts = new int[h];

        float xsum = 0f, ysum = 0f;
        int down = 0;
        for (int y = h - 1; y >= 0; y--)
        {
            downCounts[y] = down;
            int rowCount = 0;
            ReadOnlySpan<uint> row = bitmap.Row(y);
            for (int x = 0; x < wpl; x++)
            {
                uint word = row[x];
                int b = (int)(word & 0xff);
                rowCount += SumTable[b];
                xsum += CentroidTable[b] + (x * 32 + 24) * SumTable[b];
                b = (int)((word >> 8) & 0xff);
                rowCount += SumTable[b];
                xsum += CentroidTable[b] + (x * 32 + 16) * SumTable[b];
                b = (int)((word >> 16) & 0xff);
                rowCount += SumTable[b];
                xsum += CentroidTable[b] + (x * 32 + 8) * SumTable[b];
                b = (int)((word >> 24) & 0xff);
                rowCount += SumTable[b];
                xsum += CentroidTable[b] + x * 32 * SumTable[b];
            }
            down += rowCount;
            ysum += rowCount * y;
        }

        area = down;
        if (down > 0)
        {
            centroidX = xsum / down;
            centroidY = ysum / down;
        }
        else
        {
            // Cannot happen for a real component; mirrors Leptonica's fallback.
            centroidX = bitmap.Width / 2;
            centroidY = bitmap.Height / 2;
        }
    }

    /// <summary>
    /// Refines the placement of an exemplar by trying +/-1 pixel shifts and keeping the one that differs least from
    /// the page content (finalPositioningForAlignment).
    /// </summary>
    private static void FinalPositioning(BinaryBitmap page, int x, int y, int idelx, int idely, BinaryBitmap template, out int dx, out int dy)
    {
        int w = template.Width, h = template.Height;
        BinaryBitmap window = page.Clip(x - idelx - Border, y - idely - Border, w, h);

        var scratch = new BinaryBitmap(w, h);
        int minCount = int.MaxValue;
        dx = 0;
        dy = 0;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                window.Words.CopyTo(scratch.Words);
                scratch.Combine(BinaryBitmap.RasterOp.Xor, j, i, w, h, template, 0, 0);
                int count = scratch.CountBlackPixels();
                if (count < minCount)
                {
                    minCount = count;
                    dx = j;
                    dy = i;
                }
            }
        }
    }

    private static int[] BuildSumTable()
    {
        var table = new int[256];
        for (int i = 0; i < 256; i++) table[i] = BitOperations.PopCount((uint)i);
        return table;
    }

    /// <summary>Sum of the positions of the set bits of a byte, MSB = position 0 (makePixelCentroidTab8).</summary>
    private static int[] BuildCentroidTable()
    {
        var table = new int[256];
        for (int i = 0; i < 256; i++)
        {
            int sum = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((i & (0x80 >> bit)) != 0) sum += bit;
            }
            table[i] = sum;
        }
        return table;
    }
}
