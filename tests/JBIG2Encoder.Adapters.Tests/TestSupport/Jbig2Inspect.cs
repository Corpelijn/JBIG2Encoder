using System.Buffers.Binary;

namespace JBIG2Encoder.Adapters.Tests.TestSupport;

/// <summary>Minimal reading of the structures the tests need to look at.</summary>
internal static class Jbig2Inspect
{
    /// <summary>Reads width, height and resolution (pixels per metre) from the page information segment of a standalone file.</summary>
    public static (int Width, int Height, uint XRes, uint YRes) PageInformation(byte[] standaloneFile)
    {
        // 13-byte file header, then the first segment: 4 (number) + 1 (flags) + 1 (refs) + 1 (page) + 4 (length) = 11.
        int data = 13 + 11;
        var span = standaloneFile.AsSpan();
        return (
            (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(data, 4)),
            (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(data + 4, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(data + 8, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(data + 12, 4)));
    }
}
