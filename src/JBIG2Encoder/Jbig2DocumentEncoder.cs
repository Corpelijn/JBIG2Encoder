// Ported from jbig2enc (jbig2enc.cc: jbig2ctx, jbig2_add_page, jbig2_pages_complete, jbig2_produce_page)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;
using System.Collections.Generic;
using JBIG2Encoder.Segments;
using JBIG2Encoder.Symbols;

namespace JBIG2Encoder;

/// <summary>
/// Lossy multi-page encoder using symbol dictionaries and text regions. Add every page, then call
/// <see cref="Finish"/>. Symbols that occur on more than one page go into a shared dictionary; symbols that occur
/// only once are kept with their page, which keeps the shared dictionary small (some PDF readers decode it for every
/// page).
/// </summary>
/// <remarks>
/// Pages are classified as they are added and are not retained, so memory use grows with the number of distinct
/// symbols, not with the number of pages.
/// </remarks>
public sealed class Jbig2DocumentEncoder
{
    private readonly Jbig2Options _options;
    private readonly CorrelationClassifier _classifier;
    private readonly List<(int Width, int Height, int XDpi, int YDpi)> _pages = new();
    private bool _finished;

    /// <summary>Creates an encoder. Only the symbol-related options and packaging options apply.</summary>
    public Jbig2DocumentEncoder(Jbig2Options? options = null)
    {
        // Copied so that later changes to the caller's object cannot alter a document that is half built.
        _options = options?.Clone() ?? new Jbig2Options();
        _options.Validate();
        _classifier = new CorrelationClassifier(_options.SymbolThreshold, _options.SymbolWeight);
    }

    /// <summary>Number of pages added so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Classifies the shapes on a page. Pages can have different sizes.</summary>
    public void AddPage(BinaryBitmap page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_finished) throw new InvalidOperationException("Finish() has already been called.");

        _classifier.AddPage(page);
        _pages.Add((page.Width, page.Height, _options.ResolveXDpi(page), _options.ResolveYDpi(page)));
    }

    /// <summary>Completes the document and returns the encoded streams. Can be called once.</summary>
    public Jbig2Document Finish()
    {
        if (_finished) throw new InvalidOperationException("Finish() has already been called.");
        if (_pages.Count == 0) throw new InvalidOperationException("At least one page is required.");
        _finished = true;

        _classifier.Finish();
        if (_options.AutoThreshold)
        {
            if (_options.AutoThresholdUsesHash) TemplateMerger.MergeUsingHash(_classifier);
            else TemplateMerger.MergeAllPairs(_classifier);
        }

        IReadOnlyList<SymbolTemplate> templates = _classifier.Templates;
        IReadOnlyList<int> componentClass = _classifier.ComponentClass;
        int pageCount = _pages.Count;
        bool singlePage = pageCount == 1;
        bool full = _options.FullHeaders;

        // How often each symbol is used decides where it lives.
        var useCount = new int[templates.Count];
        foreach (int c in componentClass) useCount[c]++;

        var shared = new List<int>();
        for (int t = 0; t < templates.Count; t++)
        {
            if (useCount[t] == 0) throw new InvalidOperationException("Internal error: unused symbol.");
            if (useCount[t] > 1 || singlePage) shared.Add(t);
        }

        // Component numbers of every page and the symbols only that page uses.
        var pageComponents = new List<int>[pageCount];
        var pageOnlySymbols = new List<int>[pageCount];
        for (int p = 0; p < pageCount; p++)
        {
            pageComponents[p] = new List<int>();
            pageOnlySymbols[p] = new List<int>();
        }
        for (int c = 0; c < componentClass.Count; c++)
        {
            int page = _classifier.ComponentPage[c];
            pageComponents[page].Add(c);
            if (useCount[componentClass[c]] == 1 && !singlePage) pageOnlySymbols[page].Add(componentClass[c]);
        }

        (int X, int Y)[] lowerLeft = _classifier.GetLowerLeftCorners();
        uint segmentNumber = 0;

        // ---- The shared symbol dictionary (with the file header for standalone files).
        var globalMap = new Dictionary<int, int>();
        uint? globalSegment = null;
        var globalsWriter = new ByteWriter();
        if (full) Structures.WriteFileHeader(globalsWriter, pageCount);
        if (shared.Count > 0)
        {
            byte[] coded = SymbolCoding.EncodeSymbolDictionary(templates, shared, globalMap);
            globalSegment = segmentNumber;
            new SegmentHeader
            {
                Number = segmentNumber++,
                Type = SegmentType.SymbolDictionary,
                Page = 0,
                RetainBits = 1,
                DataLength = (uint)(Structures.SymbolDictionaryHeaderSize + coded.Length),
            }.Write(globalsWriter);
            Structures.WriteSymbolDictionaryHeader(globalsWriter, shared.Count);
            globalsWriter.WriteBytes(coded);
        }
        byte[]? globals = globalsWriter.Length > 0 ? globalsWriter.ToArray() : null;

        // ---- One stream per page.
        var pageStreams = new List<byte[]>(pageCount);
        for (int p = 0; p < pageCount; p++)
        {
            (int width, int height, int xDpi, int yDpi) = _pages[p];
            uint association = full ? (uint)(p + 1) : 1u; // in a PDF every page stream is "page 1"
            var w = new ByteWriter();

            new SegmentHeader
            {
                Number = segmentNumber++,
                Type = SegmentType.PageInformation,
                Page = association,
                DataLength = Structures.PageInfoSize,
            }.Write(w);
            Structures.WritePageInformation(w, width, height,
                Structures.DpiToPixelsPerMetre(xDpi), Structures.DpiToPixelsPerMetre(yDpi), eventuallyLossless: false);

            List<int> components = pageComponents[p];
            if (components.Count > 0)
            {
                var referred = new List<uint>();
                if (globalSegment.HasValue) referred.Add(globalSegment.Value);

                // Symbols only this page uses get a dictionary of their own, placed right before the text region.
                var localMap = new Dictionary<int, int>();
                List<int> local = pageOnlySymbols[p];
                if (local.Count > 0)
                {
                    byte[] localCoded = SymbolCoding.EncodeSymbolDictionary(templates, local, localMap);
                    referred.Add(segmentNumber);
                    new SegmentHeader
                    {
                        Number = segmentNumber++,
                        Type = SegmentType.SymbolDictionary,
                        Page = association,
                        RetainBits = 1,
                        DataLength = (uint)(Structures.SymbolDictionaryHeaderSize + localCoded.Length),
                    }.Write(w);
                    Structures.WriteSymbolDictionaryHeader(w, local.Count);
                    w.WriteBytes(localCoded);
                }

                int available = shared.Count + local.Count;
                byte[] textCoded = SymbolCoding.EncodeTextRegion(
                    globalMap, localMap, components, lowerLeft, componentClass, templates, Log2Up(available));

                var textHeader = new SegmentHeader
                {
                    Number = segmentNumber++,
                    Type = SegmentType.ImmediateTextRegion,
                    Page = association,
                    RetainBits = globalSegment.HasValue ? 2 : 0, // keep the shared dictionary for the following pages
                    DataLength = (uint)(Structures.TextRegionHeaderSize + textCoded.Length),
                };
                textHeader.ReferredTo.AddRange(referred);
                textHeader.Write(w);
                Structures.WriteTextRegionHeader(w, width, height, components.Count);
                w.WriteBytes(textCoded);
            }

            if (full)
            {
                new SegmentHeader { Number = segmentNumber++, Type = SegmentType.EndOfPage, Page = association }.Write(w);
                if (p == pageCount - 1)
                {
                    new SegmentHeader { Number = segmentNumber++, Type = SegmentType.EndOfFile, Page = 0 }.Write(w);
                }
            }

            pageStreams.Add(w.ToArray());
        }

        return new Jbig2Document(globals, pageStreams, _options.Output);
    }

    /// <summary>The number of bits needed to number <paramref name="count"/> symbols: ceil(log2(count)).</summary>
    internal static int Log2Up(int count)
    {
        int bits = 0;
        bool isPowerOfTwo = (count & (count - 1)) == 0;
        for (int v = count; (v >>= 1) != 0;) bits++;
        return isPowerOfTwo ? bits : bits + 1;
    }
}
