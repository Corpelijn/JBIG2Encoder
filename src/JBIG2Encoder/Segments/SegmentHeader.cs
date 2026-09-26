// Ported from jbig2enc (jbig2segments.h, jbig2structs.h)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;
using System.Collections.Generic;

namespace JBIG2Encoder.Segments;

/// <summary>Segment type numbers used by the encoder (T.88 7.3).</summary>
internal static class SegmentType
{
    public const int SymbolDictionary = 0;
    public const int ImmediateTextRegion = 6;
    public const int ImmediateGenericRegion = 38;
    public const int PageInformation = 48;
    public const int EndOfPage = 49;
    public const int EndOfFile = 51;
}

/// <summary>
/// A JBIG2 segment header (T.88 7.2). Only the fields the encoder needs are represented.
/// </summary>
internal sealed class SegmentHeader
{
    /// <summary>Segment number.</summary>
    public uint Number { get; set; }

    /// <summary>One of <see cref="SegmentType"/>.</summary>
    public int Type { get; set; }

    public bool DeferredNonRetain { get; set; }

    /// <summary>Retain-bit flags (5 bits): bit 0 is this segment, bits 1..4 the referred-to segments.</summary>
    public int RetainBits { get; set; }

    /// <summary>Segment numbers referred to (at most 4 are supported).</summary>
    public List<uint> ReferredTo { get; } = new();

    /// <summary>Page association (0 = none / global).</summary>
    public uint Page { get; set; }

    /// <summary>Length of the segment data that follows the header.</summary>
    public uint DataLength { get; set; }

    // Segments can only refer to earlier segments, so the size of a reference is
    // determined by this segment's own number (7.2.5).
    private int ReferenceSize => Number <= 256 ? 1 : Number <= 65536 ? 2 : 4;

    // 7.2.6
    private int PageAssociationSize => Page <= 255 ? 1 : 4;

    public void Write(ByteWriter writer)
    {
        if (ReferredTo.Count > 4)
        {
            throw new InvalidOperationException("Segments referring to more than four other segments are not supported.");
        }

        writer.WriteUInt32(Number);

        int flags = (Type & 0x3f) | (PageAssociationSize == 4 ? 0x40 : 0) | (DeferredNonRetain ? 0x80 : 0);
        writer.WriteByte(flags);

        writer.WriteByte(((ReferredTo.Count & 7) << 5) | (RetainBits & 0x1f));

        int refSize = ReferenceSize;
        foreach (uint referred in ReferredTo)
        {
            if (refSize == 4) writer.WriteUInt32(referred);
            else if (refSize == 2) writer.WriteUInt16((int)referred);
            else writer.WriteByte((int)referred);
        }

        if (PageAssociationSize == 4) writer.WriteUInt32(Page);
        else writer.WriteByte((int)Page);

        writer.WriteUInt32(DataLength);
    }
}
