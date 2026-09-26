// Ported from jbig2enc (jbig2enc.cc: jbig2_encode_generic)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;
using JBIG2Encoder.Arithmetic;
using JBIG2Encoder.Segments;

namespace JBIG2Encoder;

/// <summary>Entry points for encoding a single page.</summary>
public static class Jbig2Encoder
{
    /// <summary>
    /// Encodes a single page in the mode selected by <see cref="Jbig2Options.Mode"/>: lossless generic region coding
    /// (default) or lossy symbol coding.
    /// </summary>
    /// <returns>
    /// The streams to store. For a standalone file use <see cref="Jbig2Document.ToFile"/>; for PDF use
    /// <see cref="Jbig2Document.Globals"/> and <see cref="Jbig2Document.Pages"/>.
    /// </returns>
    public static Jbig2Document Encode(BinaryBitmap bitmap, Jbig2Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        options ??= new Jbig2Options();
        options.Validate();

        if (options.Mode == Jbig2Mode.Lossless)
        {
            return new Jbig2Document(null, new[] { EncodeGenericRegion(bitmap, options) }, options.Output);
        }

        var encoder = new Jbig2DocumentEncoder(options);
        encoder.AddPage(bitmap);
        return encoder.Finish();
    }

    /// <summary>
    /// Encodes a bitmap as a single JBIG2 generic region. This is <b>lossless</b>: decoding yields exactly the
    /// input pixels.
    /// </summary>
    /// <param name="bitmap">The bi-level image (black = foreground).</param>
    /// <param name="options">Encoding options; <c>null</c> for the defaults (standalone file, no TPGD).</param>
    /// <returns>The encoded bytes; see <see cref="Jbig2Options.Output"/> for the packaging.</returns>
    public static byte[] EncodeGenericRegion(BinaryBitmap bitmap, Jbig2Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        options ??= new Jbig2Options();
        options.Validate();

        var coder = new ArithmeticEncoder();
        coder.EncodeBitmap(bitmap, options.DuplicateLineRemoval);
        coder.Finish();
        ReadOnlySpan<byte> coded = coder.AsSpan();

        bool full = options.FullHeaders;
        var writer = new ByteWriter(coded.Length + 128);

        if (full) Structures.WriteFileHeader(writer, 1);

        uint segmentNumber = 0;

        new SegmentHeader
        {
            Number = segmentNumber++,
            Type = SegmentType.PageInformation,
            Page = 1,
            DataLength = Structures.PageInfoSize,
        }.Write(writer);
        Structures.WritePageInformation(
            writer,
            bitmap.Width,
            bitmap.Height,
            Structures.DpiToPixelsPerMetre(options.ResolveXDpi(bitmap)),
            Structures.DpiToPixelsPerMetre(options.ResolveYDpi(bitmap)),
            eventuallyLossless: true);

        new SegmentHeader
        {
            Number = segmentNumber++,
            Type = SegmentType.ImmediateGenericRegion,
            Page = 1,
            DataLength = (uint)(Structures.GenericRegionHeaderSize + coded.Length),
        }.Write(writer);
        Structures.WriteGenericRegionHeader(writer, bitmap.Width, bitmap.Height, options.DuplicateLineRemoval);
        writer.WriteBytes(coded);

        if (full)
        {
            new SegmentHeader { Number = segmentNumber++, Type = SegmentType.EndOfPage, Page = 1 }.Write(writer);
            new SegmentHeader { Number = segmentNumber++, Type = SegmentType.EndOfFile, Page = 0 }.Write(writer);
        }

        return writer.ToArray();
    }
}
