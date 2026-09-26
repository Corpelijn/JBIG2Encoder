using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

/// <summary>Degenerate inputs that commonly break encoders.</summary>
public class EdgeCaseTests
{
    private static Jbig2Options Lossy(Jbig2Output output = Jbig2Output.StandaloneFile) => new() { Mode = Jbig2Mode.Lossy, Output = output };

    private static BinaryBitmap RoundTrip(BinaryBitmap page, Jbig2Options options)
    {
        Jbig2Document doc = Jbig2Encoder.Encode(page, options);
        return options.Output == Jbig2Output.StandaloneFile
            ? Oracles.DecodeWithJBig2Decoder(doc.ToFile())
            : Oracles.DecodeWithJBig2Decoder(doc.Pages.Single(), doc.Globals);
    }

    [Fact]
    public void SinglePixelPage()
    {
        var page = new BinaryBitmap(1, 1);
        page.SetPixel(0, 0, true);

        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy())));
        Assert.True(page.PixelsEqual(RoundTrip(page, new Jbig2Options())));
    }

    [Fact]
    public void CompletelyBlackPageIsOneHugeSymbol()
    {
        BinaryBitmap page = TestImages.Solid(200, 100);
        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy())));
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(300, 1)]
    [InlineData(2, 2)]
    public void HairlinesAndTinyPages(int w, int h)
    {
        BinaryBitmap page = TestImages.Solid(w, h);
        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy())));
        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy(Jbig2Output.PdfEmbedded))));
    }

    [Fact]
    public void CheckerboardOfSinglePixelComponents()
    {
        // 2048 identical 1x1 components: one symbol used a couple of thousand times.
        var page = new BinaryBitmap(128, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 128; x++)
                if (((x + y) & 1) == 0 && (x % 2 == 0) && (y % 2 == 0)) page.SetPixel(x, y, true);
        // (the parity mask above spaces the pixels so they do not touch diagonally)
        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy())));
    }

    [Fact]
    public void GlyphsTouchingEveryPageEdge()
    {
        var page = new BinaryBitmap(120, 90);
        void Stamp(int gx, int gy)
        {
            for (int y = 0; y < 9; y++)
                for (int x = 0; x < 7; x++)
                    if (x == 0 || x == 6 || y == 0 || y == 8 || x == y) page.SetPixel(gx + x, gy + y, true);
        }
        Stamp(0, 0);            // top-left corner
        Stamp(113, 0);          // top-right
        Stamp(0, 81);           // bottom-left
        Stamp(113, 81);         // bottom-right
        Stamp(50, 0);           // top edge
        Stamp(50, 81);          // bottom edge
        Stamp(0, 40);           // left edge
        Stamp(113, 40);         // right edge
        Stamp(60, 40);          // interior

        Assert.True(page.PixelsEqual(RoundTrip(page, Lossy())));
    }

    [Fact]
    public void ManyPagesOfWhichMostAreBlank()
    {
        var encoder = new Jbig2DocumentEncoder(Lossy(Jbig2Output.PdfEmbedded));
        var pages = new List<BinaryBitmap>();
        for (int i = 0; i < 6; i++)
        {
            BinaryBitmap p = i == 4 ? TestImages.TextPage(200, 100, 2, seed: 5) : new BinaryBitmap(200, 100);
            pages.Add(p);
            encoder.AddPage(p);
        }
        Jbig2Document doc = encoder.Finish();

        for (int i = 0; i < pages.Count; i++)
        {
            Assert.True(pages[i].PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages[i], doc.Globals)), $"page {i}");
        }
    }

    [Fact]
    public void StandaloneMultiPageFileHasTheDeclaredNumberOfPagesAndIsCorrectlyFramed()
    {
        var encoder = new Jbig2DocumentEncoder(Lossy());
        for (int i = 0; i < 3; i++) encoder.AddPage(TestImages.TextPage(150, 100, 2, seed: 30 + i));
        byte[] file = encoder.Finish().ToFile();

        // Magic, flags (sequential, page count known) and the page count.
        Assert.Equal(new byte[] { 0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0, 0, 0, 3 }, file.Take(13).ToArray());
        // Ends with an end-of-file segment header (11 bytes): number, type 51, no references, page 0, length 0.
        int eof = file.Length - 11;
        Assert.Equal(51, file[eof + 4]);
        Assert.Equal(0, file[eof + 6]);                                  // page association 0
        Assert.Equal(0u, BitConverter.ToUInt32(file, file.Length - 4));  // no data

        // The first page can be decoded from the standalone file.
        Assert.NotNull(Oracles.DecodeWithJBig2Decoder(file));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Jbig2Encoder.EncodeGenericRegion(null!));
        Assert.Throws<ArgumentNullException>(() => Jbig2Encoder.Encode(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryBitmap(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryBitmap(5, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Encoder.Encode(new BinaryBitmap(4, 4), new Jbig2Options { SymbolThreshold = 0.1f }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Encoder.Encode(new BinaryBitmap(4, 4), new Jbig2Options { SymbolWeight = 2f }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Encoder.Encode(new BinaryBitmap(4, 4), new Jbig2Options { Dpi = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Encoder.Encode(new BinaryBitmap(4, 4), new Jbig2Options { Dpi = int.MaxValue }));

        var encoder = new Jbig2DocumentEncoder();
        Assert.Throws<InvalidOperationException>(() => encoder.Finish());            // no pages
        encoder.AddPage(new BinaryBitmap(4, 4));
        encoder.Finish();
        Assert.Throws<InvalidOperationException>(() => encoder.AddPage(new BinaryBitmap(4, 4)));
        Assert.Throws<InvalidOperationException>(() => encoder.Finish());
    }

    [Fact]
    public void ChangingOptionsAfterCreatingTheEncoderDoesNotAffectIt()
    {
        var options = Lossy(Jbig2Output.PdfEmbedded);
        var encoder = new Jbig2DocumentEncoder(options);
        encoder.AddPage(TestImages.TextPage(200, 100, 2, seed: 8));

        options.Output = Jbig2Output.StandaloneFile; // must not turn the half-built document into a standalone file
        options.SymbolThreshold = 0.5f;
        Jbig2Document doc = encoder.Finish();

        Assert.Equal(Jbig2Output.PdfEmbedded, doc.Output);
        Assert.NotEqual(0x97, doc.Pages[0][0]);
    }

    [Fact]
    public void ResolutionIsWrittenAsPixelsPerMetre()
    {
        var page = new BinaryBitmap(10, 10) { XResolutionDpi = 300, YResolutionDpi = 600 };
        byte[] file = Jbig2Encoder.EncodeGenericRegion(page);
        // Page information data starts after the 13-byte file header and an 11-byte segment header;
        // width, height, then x and y resolution.
        Assert.Equal(11811u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(13 + 11 + 8, 4)));
        Assert.Equal(23622u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(13 + 11 + 12, 4)));

        // The option overrides the bitmap's own resolution.
        byte[] overridden = Jbig2Encoder.EncodeGenericRegion(page, new Jbig2Options { Dpi = 150 });
        Assert.Equal(5906u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(overridden.AsSpan(13 + 11 + 8, 4)));
    }

    [Fact]
    public void BitmapImportExportRoundTrips()
    {
        var page = TestImages.Noise(37, 11, 0.4, 5);
        foreach (bool oneIsBlack in new[] { true, false })
        {
            byte[] packed = page.ToPackedBits(oneIsBlack, out int stride);
            Assert.Equal(5, stride);
            BinaryBitmap back = BinaryBitmap.FromPackedBits(packed, 37, 11, stride, oneIsBlack);
            Assert.True(page.PixelsEqual(back));
        }

        // Padding bits are ignored on import.
        byte[] dirty = page.ToPackedBits(true, out int s);
        for (int y = 0; y < 11; y++) dirty[y * s + s - 1] |= 0x07;
        Assert.True(page.PixelsEqual(BinaryBitmap.FromPackedBits(dirty, 37, 11, s, true)));
    }
}
