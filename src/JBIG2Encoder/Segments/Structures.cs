// Ported from jbig2enc (jbig2structs.h, jbig2enc.cc)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;

namespace JBIG2Encoder.Segments;

/// <summary>
/// Writers for the fixed-layout structures of a JBIG2 stream. The C++ original serialised packed
/// structs; here every field is written explicitly in big-endian order as T.88 requires.
/// </summary>
internal static class Structures
{
    /// <summary>The 8-byte JBIG2 file magic (D.4.1).</summary>
    private static ReadOnlySpan<byte> FileMagic => new byte[] { 0x97, 0x4a, 0x42, 0x32, 0x0d, 0x0a, 0x1a, 0x0a };

    public const int PageInfoSize = 19;
    public const int GenericRegionHeaderSize = 26;
    public const int SymbolDictionaryHeaderSize = 18;
    public const int TextRegionHeaderSize = 23;

    /// <summary>Converts dots per inch to the pixels-per-metre unit JBIG2 page information uses (0 = unknown).</summary>
    public static uint DpiToPixelsPerMetre(int dpi) => dpi <= 0 ? 0u : (uint)Math.Round(dpi / 0.0254);

    /// <summary>File header for the sequential file organisation (D.4).</summary>
    public static void WriteFileHeader(ByteWriter w, int pageCount)
    {
        w.WriteBytes(FileMagic);
        w.WriteByte(0x01); // bit 0: sequential organisation; bit 1: number of pages is known
        w.WriteUInt32((uint)pageCount);
    }

    /// <summary>Page information segment data (7.4.8).</summary>
    public static void WritePageInformation(ByteWriter w, int width, int height, uint xRes, uint yRes, bool eventuallyLossless)
    {
        w.WriteUInt32((uint)width);
        w.WriteUInt32((uint)height);
        w.WriteUInt32(xRes);
        w.WriteUInt32(yRes);
        w.WriteByte(eventuallyLossless ? 0x01 : 0x00); // page flags
        w.WriteUInt16(0);                             // striping information: not striped
    }

    /// <summary>Region segment information field (7.4.1); the region always covers the whole page and is ORed in.</summary>
    private static void WriteRegionInformation(ByteWriter w, int width, int height)
    {
        w.WriteUInt32((uint)width);
        w.WriteUInt32((uint)height);
        w.WriteUInt32(0);  // x location
        w.WriteUInt32(0);  // y location
        w.WriteByte(0);    // external combination operator: OR
    }

    /// <summary>
    /// Generic region segment data header (7.4.6): template 0, arithmetic coding, default adaptive template pixels.
    /// </summary>
    public static void WriteGenericRegionHeader(ByteWriter w, int width, int height, bool typicalPrediction)
    {
        WriteRegionInformation(w, width, height);
        w.WriteByte(typicalPrediction ? 0x08 : 0x00); // MMR = 0, GBTEMPLATE = 0, TPGDON
        WriteDefaultAdaptiveTemplatePixels(w);
    }

    /// <summary>The A1..A4 adaptive template pixel positions of template 0 that this encoder codes against.</summary>
    private static void WriteDefaultAdaptiveTemplatePixels(ByteWriter w)
    {
        w.WriteSByte(3);
        w.WriteSByte(-1);
        w.WriteSByte(-3);
        w.WriteSByte(-1);
        w.WriteSByte(2);
        w.WriteSByte(-2);
        w.WriteSByte(-2);
        w.WriteSByte(-2);
    }

    /// <summary>
    /// Symbol dictionary segment data header (7.4.2): arithmetic coding, no refinement/aggregation, template 0,
    /// all symbols exported.
    /// </summary>
    public static void WriteSymbolDictionaryHeader(ByteWriter w, int symbolCount)
    {
        w.WriteUInt16(0); // flags: SDHUFF = 0, SDREFAGG = 0, SDTEMPLATE = 0, no bitmap context reuse
        WriteDefaultAdaptiveTemplatePixels(w);
        w.WriteUInt32((uint)symbolCount); // SDNUMEXSYMS
        w.WriteUInt32((uint)symbolCount); // SDNUMNEWSYMS
    }

    /// <summary>
    /// Text region segment data header (7.4.3): arithmetic coding, no refinement, one-pixel strips,
    /// reference corner = bottom-left, not transposed, ORed into the page.
    /// </summary>
    public static void WriteTextRegionHeader(ByteWriter w, int width, int height, int instanceCount)
    {
        WriteRegionInformation(w, width, height);
        w.WriteUInt16(0); // flags: SBHUFF = 0, SBREFINE = 0, LOGSBSTRIPS = 0, REFCORNER = BOTTOMLEFT, ...
        w.WriteUInt32((uint)instanceCount); // SBNUMINSTANCES
    }
}
