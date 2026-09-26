using JBIG2Encoder.Adapters.Tests.TestSupport;
using JBIG2Encoder.ImageSharp;
using JBIG2Encoder.Tests.TestSupport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JBIG2Encoder.Adapters.Tests;

public class ImageSharpAdapterTests
{
    private static Image<Rgba32> ToImage(BinaryBitmap ink, Configuration? configuration = null)
    {
        var image = configuration == null ? new Image<Rgba32>(ink.Width, ink.Height) : new Image<Rgba32>(configuration, ink.Width, ink.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) row[x] = ink.GetPixel(x, y) ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
            }
        });
        return image;
    }

    [Fact]
    public void SaveAsJbig2IsLosslessForBlackAndWhiteInput()
    {
        BinaryBitmap ink = TestImages.TextPage(400, 300, 2, seed: 1);
        using Image<Rgba32> image = ToImage(ink);

        using var ms = new MemoryStream();
        image.SaveAsJbig2(ms);

        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(ms.ToArray())));
    }

    [Fact]
    public void RegisteredFormatWorksWithSaveByFileExtension()
    {
        var configuration = Configuration.Default.Clone();
        configuration.AddJbig2Encoder();

        BinaryBitmap ink = TestImages.Geometry(160, 90);
        using Image<Rgba32> image = ToImage(ink, configuration);

        string path = Path.Combine(Path.GetTempPath(), "jbig2enc-" + Guid.NewGuid().ToString("N") + ".jb2");
        try
        {
            image.Save(path);
            Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(File.ReadAllBytes(path))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FormatIsKnownByExtensionAndMimeTypeOnceRegistered()
    {
        var configuration = Configuration.Default.Clone();
        Assert.False(configuration.ImageFormatsManager.TryFindFormatByFileExtension("jb2", out _));

        configuration.AddJbig2Encoder();

        Assert.True(configuration.ImageFormatsManager.TryFindFormatByFileExtension("jb2", out var byJb2));
        Assert.True(configuration.ImageFormatsManager.TryFindFormatByFileExtension("jbig2", out _));
        Assert.True(configuration.ImageFormatsManager.TryFindFormatByMimeType("image/x-jbig2", out _));
        Assert.Same(Jbig2Format.Instance, byJb2);
        Assert.IsType<Jbig2ImageEncoder>(configuration.ImageFormatsManager.GetEncoder(Jbig2Format.Instance));
    }

    [Fact]
    public async Task AsyncSaveProducesTheSameBytes()
    {
        BinaryBitmap ink = TestImages.Geometry(100, 60);
        using Image<Rgba32> image = ToImage(ink);

        using var sync = new MemoryStream();
        image.SaveAsJbig2(sync);
        using var async = new MemoryStream();
        await image.SaveAsJbig2Async(async);

        Assert.Equal(sync.ToArray(), async.ToArray());
    }

    [Fact]
    public void AllCommonPixelFormatsProduceTheSameOutput()
    {
        BinaryBitmap ink = TestImages.TextPage(200, 120, 2, seed: 2);
        using Image<Rgba32> reference = ToImage(ink);

        byte[] Encode<TPixel>(Image<TPixel> image) where TPixel : unmanaged, IPixel<TPixel>
        {
            using var ms = new MemoryStream();
            image.SaveAsJbig2(ms);
            return ms.ToArray();
        }

        byte[] expected = Encode(reference);
        Assert.Equal(expected, Encode(reference.CloneAs<Rgb24>()));
        Assert.Equal(expected, Encode(reference.CloneAs<Bgra32>()));
        Assert.Equal(expected, Encode(reference.CloneAs<Bgr24>()));
        Assert.Equal(expected, Encode(reference.CloneAs<L8>()));
        Assert.Equal(expected, Encode(reference.CloneAs<La16>()));
        Assert.Equal(expected, Encode(reference.CloneAs<Rgba64>()));   // fallback path through 8-bit RGBA
        Assert.Equal(expected, Encode(reference.CloneAs<L16>()));
    }

    [Fact]
    public void TransparentPixelsAreTreatedAsWhite()
    {
        using var image = new Image<Rgba32>(20, 10);
        // Fully transparent black everywhere (the default) except one opaque black pixel.
        image[3, 4] = new Rgba32(0, 0, 0, 255);

        using var ms = new MemoryStream();
        image.SaveAsJbig2(ms);
        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(ms.ToArray());

        Assert.Equal(1, decoded.CountBlackPixels());
        Assert.True(decoded.GetPixel(3, 4));
    }

    [Fact]
    public void ResolutionMetadataIsWrittenInPixelsPerMetre()
    {
        BinaryBitmap ink = TestImages.Geometry(60, 40);
        using Image<Rgba32> image = ToImage(ink);
        image.Metadata.ResolutionUnits = SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;
        image.Metadata.HorizontalResolution = 300;
        image.Metadata.VerticalResolution = 150;

        using var ms = new MemoryStream();
        image.SaveAsJbig2(ms);
        var info = Jbig2Inspect.PageInformation(ms.ToArray());

        Assert.Equal((60, 40), (info.Width, info.Height));
        Assert.Equal(11811u, info.XRes); // 300 dpi
        Assert.Equal(5906u, info.YRes);  // 150 dpi (5905.5 rounded)
    }

    [Fact]
    public void LossyEncoderOptionsProduceASymbolCodedFile()
    {
        BinaryBitmap ink = TestImages.TextPage(500, 350, 3, seed: 3);
        using Image<Rgba32> image = ToImage(ink);

        var lossless = new Jbig2ImageEncoder();
        var lossy = new Jbig2ImageEncoder { Encoding = new Jbig2Options { Mode = Jbig2Mode.Lossy } };

        using var a = new MemoryStream();
        image.SaveAsJbig2(a, lossless);
        using var b = new MemoryStream();
        image.SaveAsJbig2(b, lossy);

        Assert.True(b.Length < a.Length, "symbol coding should be smaller for a page of repeated glyphs");
        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(b.ToArray())));
    }

    [Fact]
    public void LossyPdfEmbeddedCannotBeWrittenToASingleStream()
    {
        using Image<Rgba32> image = ToImage(TestImages.TextPage(200, 100, 2, seed: 4));
        var encoder = new Jbig2ImageEncoder { Encoding = new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded } };

        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => image.SaveAsJbig2(ms, encoder));
    }

    [Fact]
    public void LosslessPdfEmbeddedWorksAndCanBeUsedInAPdf()
    {
        BinaryBitmap ink = TestImages.TextPage(300, 200, 2, seed: 5);
        using Image<Rgba32> image = ToImage(ink);
        var encoder = new Jbig2ImageEncoder { Encoding = new Jbig2Options { Output = Jbig2Output.PdfEmbedded } };

        using var ms = new MemoryStream();
        image.SaveAsJbig2(ms, encoder);

        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(ms.ToArray())));
        if (PopplerOracle.IsAvailable)
        {
            byte[] pdf = PdfBuilder.Build(new[] { new PdfBuilder.Page(300, 200, ms.ToArray()) });
            Assert.True(ink.PixelsEqual(PopplerOracle.ExtractImages(pdf).Single()));
        }
    }

    [Fact]
    public void ToJbig2BitmapLetsYouBuildMultiPageDocuments()
    {
        BinaryBitmap p1 = TestImages.TextPage(300, 200, 2, seed: 6);
        BinaryBitmap p2 = TestImages.TextPage(300, 200, 2, seed: 7);
        using Image<Rgba32> i1 = ToImage(p1);
        using Image<Rgba32> i2 = ToImage(p2);

        var encoder = new Jbig2DocumentEncoder(new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded });
        encoder.AddPage(i1.ToJbig2Bitmap());
        encoder.AddPage(i2.ToJbig2Bitmap());
        Jbig2Document doc = encoder.Finish();

        Assert.True(p1.PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages[0], doc.Globals)));
        Assert.True(p2.PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages[1], doc.Globals)));
    }

    [Fact]
    public void BinarizationOptionsAreHonored()
    {
        // A mid-gray image: black with threshold 200, white with threshold 50.
        using var image = new Image<L8>(16, 16, new L8(120));

        byte[] Encode(int threshold)
        {
            using var ms = new MemoryStream();
            image.SaveAsJbig2(ms, new Jbig2ImageEncoder
            {
                Binarization = new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = threshold },
            });
            return ms.ToArray();
        }

        Assert.Equal(16 * 16, Oracles.DecodeWithJBig2Decoder(Encode(200)).CountBlackPixels());
        Assert.Equal(0, Oracles.DecodeWithJBig2Decoder(Encode(50)).CountBlackPixels());
    }
}
