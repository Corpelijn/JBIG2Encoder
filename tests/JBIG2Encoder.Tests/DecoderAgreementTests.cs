using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

/// <summary>
/// Differential test over a spread of random pages and settings: two unrelated decoders (a JPedal port and poppler's
/// Xpdf-derived decoder) must reconstruct exactly the same pixels from the encoder's output. This holds for lossy
/// output too, because "lossy" only describes the encoder's choice of symbols, not the decoder's job. A disagreement
/// means the stream is not valid JBIG2, however little visible difference there is.
/// </summary>
public class DecoderAgreementTests
{
    public static IEnumerable<object[]> Scenarios()
    {
        // seed, width, height, glyph scale, noise, threshold, autoThreshold, pages
        yield return new object[] { 1, 97, 61, 1, 0.000, 0.92, false, 1 };
        yield return new object[] { 2, 131, 173, 2, 0.002, 0.92, false, 1 };
        yield return new object[] { 3, 333, 222, 2, 0.010, 0.85, false, 1 };
        yield return new object[] { 4, 640, 480, 3, 0.004, 0.97, false, 1 };
        yield return new object[] { 5, 500, 300, 3, 0.003, 0.92, true, 1 };
        yield return new object[] { 6, 257, 129, 2, 0.006, 0.60, true, 1 };
        yield return new object[] { 7, 401, 297, 3, 0.002, 0.92, false, 3 };
        yield return new object[] { 8, 320, 240, 2, 0.008, 0.90, true, 4 };
        yield return new object[] { 9, 63, 500, 2, 0.001, 0.92, false, 2 };
        yield return new object[] { 10, 1000, 80, 2, 0.005, 0.92, false, 1 };
        yield return new object[] { 11, 160, 160, 4, 0.020, 0.75, false, 3 };
        yield return new object[] { 12, 800, 600, 3, 0.000, 0.92, true, 2 };
    }

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public void BothDecodersReconstructTheSamePixels(int seed, int w, int h, int scale, double noise, double threshold, bool auto, int pageCount)
    {
        Skip.IfNot(PopplerOracle.IsAvailable, "pdfimages (poppler) not found; set JBIG2_PDFIMAGES or add it to PATH.");

        var pages = new List<BinaryBitmap>();
        for (int i = 0; i < pageCount; i++)
        {
            // A different page size per page exercises per-page dimensions.
            pages.Add(TestImages.TextPage(w + i * 8, h + i * 4, scale, seed * 100 + i, noise));
        }

        var options = new Jbig2Options
        {
            Mode = Jbig2Mode.Lossy,
            Output = Jbig2Output.PdfEmbedded,
            SymbolThreshold = (float)threshold,
            AutoThreshold = auto,
        };
        var encoder = new Jbig2DocumentEncoder(options);
        foreach (BinaryBitmap p in pages) encoder.AddPage(p);
        Jbig2Document doc = encoder.Finish();

        byte[] pdf = PdfBuilder.Build(
            pages.Select((p, i) => new PdfBuilder.Page(p.Width, p.Height, doc.Pages[i])).ToList(), doc.Globals);
        List<BinaryBitmap> poppler = PopplerOracle.ExtractImages(pdf);
        Assert.Equal(pageCount, poppler.Count);

        for (int i = 0; i < pageCount; i++)
        {
            BinaryBitmap jpedal = Oracles.DecodeWithJBig2Decoder(doc.Pages[i], doc.Globals);
            Assert.True(jpedal.PixelsEqual(poppler[i]), $"seed {seed} page {i}: the two decoders disagree");

            // And the result is a faithful (if lossy) rendition: bounded difference from the source.
            int differing = 0;
            for (int y = 0; y < pages[i].Height; y++)
                for (int x = 0; x < pages[i].Width; x++)
                    if (pages[i].GetPixel(x, y) != jpedal.GetPixel(x, y)) differing++;
            Assert.True(differing <= pages[i].Width * pages[i].Height * 0.06,
                $"seed {seed} page {i}: {differing} of {pages[i].Width * pages[i].Height} pixels differ");
        }
    }

    [SkippableTheory]
    [InlineData(21, 200, 150, false)]
    [InlineData(22, 333, 77, true)]
    public void LosslessOutputAgreesToo(int seed, int w, int h, bool tpgd)
    {
        Skip.IfNot(PopplerOracle.IsAvailable, "pdfimages (poppler) not found; set JBIG2_PDFIMAGES or add it to PATH.");

        BinaryBitmap page = TestImages.Noise(w, h, 0.3, seed);
        byte[] stream = Jbig2Encoder.EncodeGenericRegion(page, new Jbig2Options { Output = Jbig2Output.PdfEmbedded, DuplicateLineRemoval = tpgd });
        byte[] pdf = PdfBuilder.Build(new[] { new PdfBuilder.Page(w, h, stream) });

        Assert.True(page.PixelsEqual(Oracles.DecodeWithJBig2Decoder(stream)));
        Assert.True(page.PixelsEqual(PopplerOracle.ExtractImages(pdf).Single()));
    }
}
