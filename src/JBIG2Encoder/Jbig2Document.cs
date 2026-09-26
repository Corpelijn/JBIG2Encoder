using System;
using System.Collections.Generic;

namespace JBIG2Encoder;

/// <summary>
/// The result of encoding one or more pages: an optional stream of shared ("global") data and one stream per page.
/// </summary>
/// <remarks>
/// For a standalone file, <see cref="ToFile"/> yields the complete file. For PDF, put <see cref="Globals"/> (when not
/// <c>null</c>) in its own stream and reference it from every page's image dictionary with
/// <c>/DecodeParms &lt;&lt; /JBIG2Globals n 0 R &gt;&gt;</c>, and use each page stream as the data of that page's
/// <c>/JBIG2Decode</c> image.
/// </remarks>
public sealed class Jbig2Document
{
    internal Jbig2Document(byte[]? globals, IReadOnlyList<byte[]> pages, Jbig2Output output)
    {
        Globals = globals;
        Pages = pages;
        Output = output;
    }

    /// <summary>
    /// Shared symbols (and, for a standalone file, the file header). <c>null</c> when nothing is shared, which is
    /// always the case for <see cref="Jbig2Mode.Lossless"/> output in <see cref="Jbig2Output.PdfEmbedded"/> form.
    /// </summary>
    public byte[]? Globals { get; }

    /// <summary>The encoded pages, in the order they were added.</summary>
    public IReadOnlyList<byte[]> Pages { get; }

    /// <summary>The packaging the document was created with.</summary>
    public Jbig2Output Output { get; }

    /// <summary>
    /// Concatenates everything into one complete JBIG2 file. Only valid for <see cref="Jbig2Output.StandaloneFile"/>.
    /// </summary>
    public byte[] ToFile()
    {
        if (Output != Jbig2Output.StandaloneFile)
        {
            throw new InvalidOperationException("Only a StandaloneFile document can be written as one file. Use Globals and Pages for PDF embedding.");
        }
        return Concatenate();
    }

    /// <summary>
    /// Returns the document as a single byte array when that is well defined: a standalone file, or an embedded
    /// document without globals. An embedded document with globals must be written as two streams.
    /// </summary>
    public byte[] ToSingleStream()
    {
        if (Output == Jbig2Output.StandaloneFile) return Concatenate();
        if (Globals == null && Pages.Count == 1) return Pages[0];

        throw new InvalidOperationException(
            "This document consists of a globals stream and page streams that PDF stores separately. " +
            "Use Jbig2Output.StandaloneFile for a single stream, or read Globals and Pages.");
    }

    private byte[] Concatenate()
    {
        int length = Globals?.Length ?? 0;
        foreach (byte[] page in Pages) length += page.Length;

        var result = new byte[length];
        int offset = 0;
        if (Globals != null)
        {
            Globals.CopyTo(result, offset);
            offset += Globals.Length;
        }
        foreach (byte[] page in Pages)
        {
            page.CopyTo(result, offset);
            offset += page.Length;
        }
        return result;
    }
}
