using JBIG2Encoder.Arithmetic;

namespace JBIG2Encoder.Tests;

public class ArithmeticEncoderTests
{
    // ITU-T T.88 Annex H.2 "Test sequence for arithmetic coder": 256 decisions coded with a
    // single context (I = 0, MPS = 0), packed MSB-first into 32 bytes.
    private static readonly byte[] H2Decisions =
    {
        0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0, 0x03, 0x52, 0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA,
        0x82, 0xC0, 0x20, 0x00, 0xFC, 0xD7, 0x9E, 0xF6, 0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46, 0xA3, 0xBF,
    };

    private static readonly byte[] H2Encoded =
    {
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00, 0x41, 0x0D, 0xBB, 0x86,
        0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47, 0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC,
    };

    [Fact]
    public void MatchesSpecificationTestSequence()
    {
        var encoder = new ArithmeticEncoder();
        var context = new byte[1];

        foreach (byte b in H2Decisions)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                encoder.EncodeBit(context, 0, (b >> bit) & 1);
            }
        }
        encoder.Finish();

        Assert.Equal(H2Encoded, encoder.ToArray());
    }

    [Fact]
    public void StreamAlwaysEndsWithMarker()
    {
        var encoder = new ArithmeticEncoder();
        encoder.EncodeInt(ArithmeticEncoder.IADH, 5);
        encoder.EncodeOob(ArithmeticEncoder.IADW);
        encoder.Finish();

        byte[] data = encoder.ToArray();
        Assert.True(data.Length >= 2);
        Assert.Equal(0xFF, data[^2]);
        Assert.Equal(0xAC, data[^1]);
    }
}
