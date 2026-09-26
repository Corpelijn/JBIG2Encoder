using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

/// <summary>
/// Embeds encoder output in a PDF and decodes it with poppler's independent JBIG2 decoder. This is the closest
/// automated proxy for "does a real PDF reader understand it".
/// </summary>
public class PdfReaderCompatibilityTests
{
    private static void RequirePoppler() => Skip.IfNot(PopplerOracle.IsAvailable, "pdfimages (poppler) not found; set JBIG2_PDFIMAGES or add it to PATH.");

    [SkippableTheory]
    [InlineData(8, 8)]
    [InlineData(31, 17)]
    [InlineData(32, 17)]
    [InlineData(33, 17)]
    [InlineData(100, 37)]
    [InlineData(257, 129)]
    [InlineData(1000, 300)]
    public void GenericRegionDecodesPixelExactInPoppler(int w, int h)
    {
        RequirePoppler();

        var images = new (string Name, BinaryBitmap Image)[]
        {
            ("noise50", TestImages.Noise(w, h, 0.5, 11)),
            ("noise5", TestImages.Noise(w, h, 0.05, 12)),
            ("geometry", TestImages.Geometry(w, h)),
            ("text", TestImages.TextPage(w, h, 2, 13)),
        };

        foreach (var (name, image) in images)
        {
            foreach (bool tpgd in new[] { false, true })
            {
                byte[] stream = Jbig2Encoder.EncodeGenericRegion(image, new Jbig2Options
                {
                    Output = Jbig2Output.PdfEmbedded,
                    DuplicateLineRemoval = tpgd,
                });
                byte[] pdf = PdfBuilder.Build(new[] { new PdfBuilder.Page(w, h, stream) });

                List<BinaryBitmap> decoded = PopplerOracle.ExtractImages(pdf);

                Assert.Single(decoded);
                Assert.True(image.PixelsEqual(decoded[0]), $"{name} {w}x{h} tpgd={tpgd}: poppler's decoded image differs from the source");
            }
        }
    }
}
