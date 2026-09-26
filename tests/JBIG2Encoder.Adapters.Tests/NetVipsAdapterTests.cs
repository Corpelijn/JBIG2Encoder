using JBIG2Encoder.Adapters.Tests.TestSupport;
using JBIG2Encoder.NetVips;
using JBIG2Encoder.Tests.TestSupport;
using NetVips;

namespace JBIG2Encoder.Adapters.Tests;

public class NetVipsAdapterTests
{
    private static Image FromInk(BinaryBitmap ink, int bands)
    {
        var bytes = new byte[ink.Width * ink.Height * bands];
        for (int y = 0; y < ink.Height; y++)
        {
            for (int x = 0; x < ink.Width; x++)
            {
                byte v = ink.GetPixel(x, y) ? (byte)0 : (byte)255;
                int o = (y * ink.Width + x) * bands;
                for (int b = 0; b < bands; b++) bytes[o + b] = v;
                if (bands == 2 || bands == 4) bytes[o + bands - 1] = 255; // opaque alpha
            }
        }
        return Image.NewFromMemory(bytes, ink.Width, ink.Height, bands, Enums.BandFormat.Uchar);
    }

    [Theory]
    [InlineData(1)] // gray
    [InlineData(2)] // gray + alpha
    [InlineData(3)] // RGB
    [InlineData(4)] // RGBA
    public void EightBitImagesAreEncodedLosslessly(int bands)
    {
        BinaryBitmap ink = TestImages.TextPage(300, 200, 2, seed: 1);
        using Image image = FromInk(ink, bands);

        byte[] jbig2 = image.Jbig2saveBuffer();

        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(jbig2)));
    }

    [Fact]
    public void SixteenBitImagesAreNormalizedByLibvips()
    {
        BinaryBitmap ink = TestImages.TextPage(200, 120, 2, seed: 2);
        var samples = new byte[ink.Width * ink.Height * 2];
        for (int y = 0; y < ink.Height; y++)
        {
            for (int x = 0; x < ink.Width; x++)
            {
                ushort v = ink.GetPixel(x, y) ? (ushort)0 : (ushort)65535;
                int o = (y * ink.Width + x) * 2;
                BitConverter.GetBytes(v).CopyTo(samples, o);
            }
        }
        using Image image = Image.NewFromMemory(samples, ink.Width, ink.Height, 1, Enums.BandFormat.Ushort)
            .Copy(interpretation: Enums.Interpretation.Grey16);

        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(image.Jbig2saveBuffer())));
    }

    [Fact]
    public void TransparentPixelsAreFlattenedOntoWhite()
    {
        var bytes = new byte[20 * 10 * 4]; // fully transparent black
        int o = (4 * 20 + 3) * 4;
        bytes[o + 3] = 255;                  // one opaque black pixel
        using Image image = Image.NewFromMemory(bytes, 20, 10, 4, Enums.BandFormat.Uchar);

        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(image.Jbig2saveBuffer());
        Assert.Equal(1, decoded.CountBlackPixels());
        Assert.True(decoded.GetPixel(3, 4));
    }

    [Fact]
    public void ImagesLoadedFromFilesWork()
    {
        // Round trip through a real image codec so we know the extension works on images libvips produced itself.
        BinaryBitmap ink = TestImages.Geometry(150, 100);
        using Image source = FromInk(ink, 3);
        byte[] png = source.PngsaveBuffer();

        using Image loaded = Image.NewFromBuffer(png);
        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(loaded.Jbig2saveBuffer())));
    }

    [Fact]
    public void ResolutionMetadataIsCarriedOver()
    {
        BinaryBitmap ink = TestImages.Geometry(60, 40);
        using Image plain = FromInk(ink, 1);
        double pixelsPerMm = 300 / 25.4;
        using Image image = plain.Copy(xres: pixelsPerMm, yres: pixelsPerMm);

        var info = Jbig2Inspect.PageInformation(image.Jbig2saveBuffer());
        Assert.Equal(11811u, info.XRes);
        Assert.Equal(11811u, info.YRes);

        // libvips' default (1 px/mm) means "not set".
        var unknown = Jbig2Inspect.PageInformation(plain.Jbig2saveBuffer());
        Assert.Equal((0u, 0u), (unknown.XRes, unknown.YRes));
    }

    [Fact]
    public void SaveToStreamAndFile()
    {
        BinaryBitmap ink = TestImages.Geometry(80, 60);
        using Image image = FromInk(ink, 3);

        using var ms = new MemoryStream();
        image.Jbig2saveStream(ms);
        Assert.Equal(image.Jbig2saveBuffer(), ms.ToArray());

        string path = Path.Combine(Path.GetTempPath(), "jbig2enc-" + Guid.NewGuid().ToString("N") + ".jb2");
        try
        {
            image.Jbig2save(path);
            Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(File.ReadAllBytes(path))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LossyMultiPageDocumentThroughToJbig2Bitmap()
    {
        BinaryBitmap p1 = TestImages.TextPage(300, 200, 2, seed: 3);
        BinaryBitmap p2 = TestImages.TextPage(300, 200, 2, seed: 4);
        using Image i1 = FromInk(p1, 3);
        using Image i2 = FromInk(p2, 1);

        var encoder = new Jbig2DocumentEncoder(new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded });
        encoder.AddPage(i1.ToJbig2Bitmap());
        encoder.AddPage(i2.ToJbig2Bitmap());
        Jbig2Document doc = encoder.Finish();

        Assert.True(p1.PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages[0], doc.Globals)));
        Assert.True(p2.PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages[1], doc.Globals)));
    }

    [Fact]
    public void SourceImageIsNotDisposedOrModified()
    {
        BinaryBitmap ink = TestImages.Geometry(50, 50);
        using Image image = FromInk(ink, 4);

        _ = image.Jbig2saveBuffer();
        _ = image.Jbig2saveBuffer(); // a second use proves the image is still valid

        Assert.Equal(4, image.Bands);
        Assert.Equal(50, image.Width);
    }
}
