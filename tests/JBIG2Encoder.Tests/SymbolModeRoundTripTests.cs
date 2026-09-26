using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

/// <summary>
/// Lossy path. Crisp pages consist of identical glyph instances, so decoding must reproduce them exactly (all
/// instances equal their exemplar and are placed on the right pixel). Anything less means a coding or placement bug.
/// </summary>
public class SymbolModeRoundTripTests
{
    private static Jbig2Options Lossy(Jbig2Output output = Jbig2Output.StandaloneFile, bool autoThreshold = false, bool hash = true) => new()
    {
        Mode = Jbig2Mode.Lossy,
        Output = output,
        AutoThreshold = autoThreshold,
        AutoThresholdUsesHash = hash,
    };

    private static void AssertSame(BinaryBitmap expected, BinaryBitmap actual, string what)
    {
        Assert.True(expected.PixelsEqual(actual), $"{what}: decoded page differs from the source");
    }

    [Theory]
    [InlineData(300, 200, 2, 1)]
    [InlineData(640, 480, 3, 2)]
    [InlineData(1000, 700, 4, 3)]
    public void StandaloneSinglePageDecodesExactly(int w, int h, int scale, int seed)
    {
        BinaryBitmap page = TestImages.TextPage(w, h, scale, seed);

        Jbig2Document doc = Jbig2Encoder.Encode(page, Lossy());
        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(doc.ToFile());

        AssertSame(page, decoded, $"{w}x{h}");
    }

    [Theory]
    [InlineData(300, 200, 2, 4)]
    [InlineData(800, 600, 3, 5)]
    public void EmbeddedSinglePageDecodesExactlyInBothDecoders(int w, int h, int scale, int seed)
    {
        BinaryBitmap page = TestImages.TextPage(w, h, scale, seed);

        Jbig2Document doc = Jbig2Encoder.Encode(page, Lossy(Jbig2Output.PdfEmbedded));
        Assert.NotNull(doc.Globals);
        Assert.Single(doc.Pages);

        AssertSame(page, Oracles.DecodeWithJBig2Decoder(doc.Pages[0], doc.Globals), "JPedal-derived decoder");

        if (PopplerOracle.IsAvailable)
        {
            byte[] pdf = PdfBuilder.Build(new[] { new PdfBuilder.Page(w, h, doc.Pages[0]) }, doc.Globals);
            List<BinaryBitmap> images = PopplerOracle.ExtractImages(pdf);
            Assert.Single(images);
            AssertSame(page, images[0], "poppler");
        }
    }

    [Fact]
    public void MultiPageDocumentSharesSymbolsAndDecodesEveryPageExactly()
    {
        // Same alphabet on every page, so most symbols are used many times and land in the shared dictionary.
        var pages = new List<BinaryBitmap>
        {
            TestImages.TextPage(500, 400, 3, seed: 10),
            TestImages.TextPage(500, 400, 3, seed: 11),
            TestImages.TextPage(420, 300, 3, seed: 12),
        };

        var encoder = new Jbig2DocumentEncoder(Lossy(Jbig2Output.PdfEmbedded));
        foreach (BinaryBitmap p in pages) encoder.AddPage(p);
        Jbig2Document doc = encoder.Finish();

        Assert.NotNull(doc.Globals);
        Assert.Equal(3, doc.Pages.Count);

        for (int i = 0; i < pages.Count; i++)
        {
            AssertSame(pages[i], Oracles.DecodeWithJBig2Decoder(doc.Pages[i], doc.Globals), $"page {i} (JPedal-derived decoder)");
        }

        if (PopplerOracle.IsAvailable)
        {
            byte[] pdf = PdfBuilder.Build(
                pages.Select((p, i) => new PdfBuilder.Page(p.Width, p.Height, doc.Pages[i])).ToList(), doc.Globals);
            List<BinaryBitmap> images = PopplerOracle.ExtractImages(pdf);
            Assert.Equal(3, images.Count);
            for (int i = 0; i < pages.Count; i++) AssertSame(pages[i], images[i], $"page {i} (poppler)");
        }
    }

    [Fact]
    public void SymbolsUsedOnlyOnceStayWithTheirPageAndStillDecode()
    {
        // Page 1 has two shapes nobody else has (crisp, and sized unlike any glyph or each other, so they cannot be
        // confused with anything); the shared alphabet is still shared. Exercises page-local dictionaries.
        BinaryBitmap common = TestImages.TextPage(400, 300, 3, seed: 30);
        BinaryBitmap withRareShape = TestImages.TextPage(400, 300, 3, seed: 31);
        for (int y = 200; y < 231; y++)                 // hollow 47 x 31 frame, 3 px thick
            for (int x = 100; x < 147; x++)
                if (x < 103 || x >= 144 || y < 203 || y >= 228) withRareShape.SetPixel(x, y, true);
        for (int y = 250; y < 285; y++)                 // 29 x 35 box crossed by both diagonals
            for (int x = 200; x < 229; x++)
                if (x == 200 || x == 228 || y == 250 || y == 284 || x - 200 == y - 250 || x - 200 == 284 - y)
                    withRareShape.SetPixel(x, y, true);

        var encoder = new Jbig2DocumentEncoder(Lossy(Jbig2Output.PdfEmbedded));
        encoder.AddPage(common);
        encoder.AddPage(withRareShape);
        Jbig2Document doc = encoder.Finish();

        AssertSame(common, Oracles.DecodeWithJBig2Decoder(doc.Pages[0], doc.Globals), "page 0");
        AssertSame(withRareShape, Oracles.DecodeWithJBig2Decoder(doc.Pages[1], doc.Globals), "page 1");
    }

    [Fact]
    public void BlankPagesInsideADocumentDecodeAsBlank()
    {
        BinaryBitmap text = TestImages.TextPage(300, 200, 2, seed: 40);
        var pages = new[] { new BinaryBitmap(300, 200), text, new BinaryBitmap(300, 200) };

        var encoder = new Jbig2DocumentEncoder(Lossy(Jbig2Output.PdfEmbedded));
        foreach (BinaryBitmap p in pages) encoder.AddPage(p);
        Jbig2Document doc = encoder.Finish();

        for (int i = 0; i < pages.Length; i++)
        {
            AssertSame(pages[i], Oracles.DecodeWithJBig2Decoder(doc.Pages[i], doc.Globals), $"page {i}");
        }
    }

    [Fact]
    public void FullyBlankDocumentHasNoGlobalsAndDecodes()
    {
        var blank = new BinaryBitmap(120, 80);
        Jbig2Document doc = Jbig2Encoder.Encode(blank, Lossy(Jbig2Output.PdfEmbedded));

        Assert.Null(doc.Globals);
        AssertSame(blank, Oracles.DecodeWithJBig2Decoder(doc.Pages[0]), "blank page");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutoThresholdKeepsOutputDecodable(bool useHash)
    {
        BinaryBitmap page = TestImages.TextPage(600, 400, 3, seed: 50, noise: 0.004);

        Jbig2Document plain = Jbig2Encoder.Encode(page, Lossy(Jbig2Output.PdfEmbedded));
        Jbig2Document merged = Jbig2Encoder.Encode(page, Lossy(Jbig2Output.PdfEmbedded, autoThreshold: true, hash: useHash));

        BinaryBitmap decodedPlain = Oracles.DecodeWithJBig2Decoder(plain.Pages[0], plain.Globals);
        BinaryBitmap decodedMerged = Oracles.DecodeWithJBig2Decoder(merged.Pages[0], merged.Globals);
        Assert.Equal(page.Width, decodedMerged.Width);

        // Merging can only reduce the dictionary.
        Assert.True(merged.Globals!.Length <= plain.Globals!.Length);
        Assert.True(Differences(page, decodedMerged) < page.Width * page.Height * 0.03);
        Assert.True(Differences(page, decodedPlain) < page.Width * page.Height * 0.03);
    }

    [Fact]
    public void NoisyPageIsCloseAndMuchSmallerThanGenericCoding()
    {
        // Glyphs with scanner-like speckle: instances differ from each other, so the classifier has real work to do.
        BinaryBitmap page = TestImages.TextPage(800, 600, 3, seed: 60, noise: 0.003);

        byte[] lossless = Jbig2Encoder.EncodeGenericRegion(page);
        Jbig2Document lossy = Jbig2Encoder.Encode(page, Lossy());
        byte[] lossyBytes = lossy.ToFile();

        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(lossyBytes);
        int differing = Differences(page, decoded);

        Assert.True(differing < page.Width * page.Height * 0.03, $"{differing} differing pixels");
        Assert.True(lossyBytes.Length < lossless.Length, $"lossy {lossyBytes.Length} bytes vs lossless {lossless.Length}");
    }

    [Fact]
    public void HigherThresholdKeepsMoreSymbolsAndIsAtLeastAsAccurate()
    {
        BinaryBitmap page = TestImages.TextPage(800, 600, 3, seed: 61, noise: 0.006);

        var loose = Lossy();
        loose.SymbolThreshold = 0.80f;
        var strict = Lossy();
        strict.SymbolThreshold = 0.97f;

        Jbig2Document looseDoc = Jbig2Encoder.Encode(page, loose);
        Jbig2Document strictDoc = Jbig2Encoder.Encode(page, strict);

        int looseErrors = Differences(page, Oracles.DecodeWithJBig2Decoder(looseDoc.ToFile()));
        int strictErrors = Differences(page, Oracles.DecodeWithJBig2Decoder(strictDoc.ToFile()));

        Assert.True(strictDoc.ToFile().Length > looseDoc.ToFile().Length);
        Assert.True(strictErrors <= looseErrors, $"strict {strictErrors} vs loose {looseErrors} differing pixels");
    }

    [Fact]
    public void LargeComponentsAreNeverDropped()
    {
        // A page-wide frame and a filled block far larger than any glyph. Leptonica's default limits would silently
        // discard these; here they must survive as ordinary symbols.
        var page = new BinaryBitmap(700, 500);
        for (int x = 5; x < 695; x++) { page.SetPixel(x, 5, true); page.SetPixel(x, 494, true); }
        for (int y = 5; y < 495; y++) { page.SetPixel(5, y, true); page.SetPixel(694, y, true); }
        for (int y = 150; y < 320; y++)
            for (int x = 200; x < 560; x++)
                page.SetPixel(x, y, true);

        Jbig2Document doc = Jbig2Encoder.Encode(page, Lossy());
        AssertSame(page, Oracles.DecodeWithJBig2Decoder(doc.ToFile()), "large components");
    }

    [Fact]
    public void EmbeddedDocumentWithGlobalsCannotBeWrittenAsOneStream()
    {
        Jbig2Document doc = Jbig2Encoder.Encode(TestImages.TextPage(200, 100, 2, seed: 70), Lossy(Jbig2Output.PdfEmbedded));
        Assert.Throws<InvalidOperationException>(() => doc.ToSingleStream());
        Assert.Throws<InvalidOperationException>(() => doc.ToFile());
    }

    private static int Differences(BinaryBitmap a, BinaryBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
        return n;
    }
}
