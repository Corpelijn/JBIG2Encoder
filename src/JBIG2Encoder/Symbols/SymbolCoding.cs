// Ported from jbig2enc (jbig2sym.cc: jbig2enc_symboltable, jbig2enc_textregion)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.
//
// The refinement branch of the original (lossless symbol coding) is deliberately not ported: upstream
// documents it as broken and it is known to crash Adobe Acrobat.

using System;
using System.Collections.Generic;
using System.Linq;
using JBIG2Encoder.Arithmetic;

namespace JBIG2Encoder.Symbols;

internal static class SymbolCoding
{
    /// <summary>
    /// Encodes a symbol dictionary containing <paramref name="symbolList"/> (indexes into <paramref name="templates"/>),
    /// all of which are exported. Symbols are stored in increasing height and, within the same height, increasing
    /// width, as the JBIG2 height-class coding requires.
    /// </summary>
    /// <param name="templates">The class exemplars.</param>
    /// <param name="symbolList">Indexes into <paramref name="templates"/> of the symbols to store.</param>
    /// <param name="symbolMap">Receives, for every template index in the list, its position in this dictionary.</param>
    public static byte[] EncodeSymbolDictionary(IReadOnlyList<SymbolTemplate> templates, IReadOnlyList<int> symbolList, Dictionary<int, int> symbolMap)
    {
        var coder = new ArithmeticEncoder();
        int n = symbolList.Count;
        int number = 0;

        // Sort by height; the original index breaks ties so the output is deterministic.
        List<int> sorted = symbolList
            .Select((t, order) => (Template: t, Order: order))
            .OrderBy(e => templates[e.Template].Height)
            .ThenBy(e => e.Order)
            .Select(e => e.Template)
            .ToList();

        int previousHeight = 0;
        for (int i = 0; i < n;)
        {
            int height = templates[sorted[i]].Height;
            int j = i + 1;
            while (j < n && templates[sorted[j]].Height == height) j++;

            // All symbols i..j-1 share this height; order them by increasing width.
            List<int> heightClass = sorted.Skip(i).Take(j - i)
                .Select((t, order) => (Template: t, Order: order))
                .OrderBy(e => templates[e.Template].Width)
                .ThenBy(e => e.Order)
                .Select(e => e.Template)
                .ToList();

            coder.EncodeInt(ArithmeticEncoder.IADH, height - previousHeight);
            previousHeight = height;

            int symbolWidth = 0;
            foreach (int t in heightClass)
            {
                int width = templates[t].Width;
                coder.EncodeInt(ArithmeticEncoder.IADW, width - symbolWidth);
                symbolWidth = width;

                coder.EncodeBitmap(templates[t].ToUnbordered(), duplicateLineRemoval: false);
                symbolMap[t] = number++;
            }

            // Out-of-band marks the end of the height class.
            coder.EncodeOob(ArithmeticEncoder.IADW);
            i = j;
        }

        // The export flags are run-length coded: a run of 0 non-exported symbols, then a run of all n symbols.
        coder.EncodeInt(ArithmeticEncoder.IAEX, 0);
        coder.EncodeInt(ArithmeticEncoder.IAEX, n);

        coder.Finish();
        return coder.ToArray();
    }

    /// <summary>
    /// Encodes a text region: every component of a page is coded as a reference to a symbol plus a position.
    /// Symbols are placed by their bottom-left corner (reference corner BOTTOMLEFT), in one-pixel-high strips.
    /// </summary>
    /// <param name="globalMap">Template index → symbol ID for the global dictionary.</param>
    /// <param name="localMap">Template index → position in the page's local dictionary (IDs follow the global symbols).</param>
    /// <param name="pageComponents">Global component numbers of this page's components.</param>
    /// <param name="lowerLeft">Lower-left corner of the placed symbol, indexed by global component number.</param>
    /// <param name="componentClass">Template index of every component.</param>
    /// <param name="templates">The class exemplars (only their widths are needed).</param>
    /// <param name="symbolCodeLength">Bits per symbol ID: ceil(log2(number of symbols available to the region)).</param>
    public static byte[] EncodeTextRegion(
        IReadOnlyDictionary<int, int> globalMap,
        IReadOnlyDictionary<int, int> localMap,
        IReadOnlyList<int> pageComponents,
        (int X, int Y)[] lowerLeft,
        IReadOnlyList<int> componentClass,
        IReadOnlyList<SymbolTemplate> templates,
        int symbolCodeLength)
    {
        var coder = new ArithmeticEncoder();

        // Top to bottom by baseline; the component number keeps the order deterministic.
        List<int> byBaseline = pageComponents
            .OrderBy(c => lowerLeft[c].Y)
            .ThenBy(c => c)
            .ToList();

        int stripTop = 0;
        int firstS = 0;

        // The initial strip position is coded as 0; the first strip then carries the real offset.
        coder.EncodeInt(ArithmeticEncoder.IADT, 0);

        int n = byBaseline.Count;
        for (int i = 0; i < n;)
        {
            // With one-pixel strips a strip is simply all symbols sharing a baseline.
            int baseline = lowerLeft[byBaseline[i]].Y;
            int j = i + 1;
            while (j < n && lowerLeft[byBaseline[j]].Y == baseline) j++;

            List<int> strip = byBaseline.Skip(i).Take(j - i)
                .OrderBy(c => lowerLeft[c].X)
                .ThenBy(c => c)
                .ToList();

            coder.EncodeInt(ArithmeticEncoder.IADT, baseline - stripTop);
            stripTop = baseline;

            int currentS = 0;
            bool firstSymbol = true;
            foreach (int component in strip)
            {
                int x = lowerLeft[component].X;
                if (firstSymbol)
                {
                    firstSymbol = false;
                    int deltaFirstS = x - firstS;
                    coder.EncodeInt(ArithmeticEncoder.IAFS, deltaFirstS);
                    firstS += deltaFirstS;
                    currentS = firstS;
                }
                else
                {
                    int deltaS = x - currentS;
                    coder.EncodeInt(ArithmeticEncoder.IADS, deltaS);
                    currentS += deltaS;
                }

                // (IAIT would go here, but with one-pixel strips every symbol is on the strip's baseline.)

                int template = componentClass[component];
                int symbolId;
                if (globalMap.TryGetValue(template, out int globalId))
                {
                    symbolId = globalId;
                }
                else if (localMap.TryGetValue(template, out int localId))
                {
                    symbolId = localId + globalMap.Count;
                }
                else
                {
                    throw new InvalidOperationException($"Symbol {template} is in no dictionary.");
                }
                coder.EncodeIaid(symbolCodeLength, symbolId);

                // After a symbol the current position moves to its right edge.
                currentS += templates[template].Width - 1;
            }

            // Out-of-band terminates the strip.
            coder.EncodeOob(ArithmeticEncoder.IADS);
            i = j;
        }

        coder.Finish();
        return coder.ToArray();
    }
}
