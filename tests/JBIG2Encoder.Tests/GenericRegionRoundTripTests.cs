using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

/// <summary>Lossless path: what goes in must come out pixel-for-pixel from an independent decoder.</summary>
public class GenericRegionRoundTripTests
{
    public static IEnumerable<object[]> Sizes() =>
        new[]
        {
            new object[] { 1, 1 },
            new object[] { 2, 3 },
            new object[] { 7, 3 },
            new object[] { 8, 8 },
            new object[] { 31, 5 },
            new object[] { 32, 5 },
            new object[] { 33, 5 },
            new object[] { 63, 9 },
            new object[] { 64, 64 },
            new object[] { 65, 3 },
            new object[] { 100, 37 },
            new object[] { 257, 129 },
            new object[] { 1000, 300 },
        };

    private static IEnumerable<(string Name, BinaryBitmap Image)> Images(int w, int h)
    {
        yield return ("blank", TestImages.Blank(w, h));
        yield return ("solid", TestImages.Solid(w, h));
        yield return ("noise50", TestImages.Noise(w, h, 0.5, seed: 1));
        yield return ("noise5", TestImages.Noise(w, h, 0.05, seed: 2));
        yield return ("geometry", TestImages.Geometry(w, h));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void StandaloneFileRoundTrips(int w, int h)
    {
        foreach (var (name, image) in Images(w, h))
        {
            foreach (bool tpgd in new[] { false, true })
            {
                byte[] encoded = Jbig2Encoder.EncodeGenericRegion(image, new Jbig2Options { DuplicateLineRemoval = tpgd });
                BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(encoded);
                Assert.True(image.PixelsEqual(decoded), $"{name} {w}x{h} tpgd={tpgd}: decoded image differs");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void PdfEmbeddedStreamRoundTrips(int w, int h)
    {
        foreach (var (name, image) in Images(w, h))
        {
            byte[] encoded = Jbig2Encoder.EncodeGenericRegion(image, new Jbig2Options { Output = Jbig2Output.PdfEmbedded });
            BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(encoded);
            Assert.True(image.PixelsEqual(decoded), $"{name} {w}x{h}: decoded image differs");
        }
    }

    [Fact]
    public void OracleIsNotVacuous()
    {
        // Guards against a harness that "passes" because the decoder returns something fixed: a corrupted
        // stream must decode to a different image (or fail outright).
        var image = TestImages.Noise(200, 100, 0.5, seed: 99);
        byte[] encoded = Jbig2Encoder.EncodeGenericRegion(image);
        encoded[encoded.Length / 2] ^= 0x5A;

        bool differs;
        try
        {
            differs = !image.PixelsEqual(Oracles.DecodeWithJBig2Decoder(encoded));
        }
        catch (Exception)
        {
            differs = true;
        }
        Assert.True(differs);
    }

    [Fact]
    public void EmbeddedStreamHasNoFileHeaderOrTrailer()
    {
        var image = TestImages.Geometry(40, 40);
        byte[] embedded = Jbig2Encoder.EncodeGenericRegion(image, new Jbig2Options { Output = Jbig2Output.PdfEmbedded });
        byte[] file = Jbig2Encoder.EncodeGenericRegion(image);

        // File magic at the start of a standalone file only.
        Assert.Equal(new byte[] { 0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A }, file.Take(8).ToArray());
        Assert.NotEqual(0x97, embedded[0]);

        // The standalone file = 13-byte header + the embedded stream + end-of-page + end-of-file segment headers,
        // except that segment numbers/associations are identical, so the embedded bytes must be a substring.
        Assert.True(file.Length > embedded.Length);
        Assert.Equal(embedded, file.Skip(13).Take(embedded.Length).ToArray());
    }
}
