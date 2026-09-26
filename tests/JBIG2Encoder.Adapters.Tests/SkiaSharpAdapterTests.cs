using System.Runtime.InteropServices;
using JBIG2Encoder.Adapters.Tests.TestSupport;
using JBIG2Encoder.SkiaSharp;
using JBIG2Encoder.Tests.TestSupport;
using SkiaSharp;

namespace JBIG2Encoder.Adapters.Tests;

public class SkiaSharpAdapterTests
{
    /// <summary>Builds an 8888 or gray bitmap with black ink on white by writing the raw bytes.</summary>
    private static SKBitmap ToSkBitmap(BinaryBitmap ink, SKColorType colorType, SKAlphaType alphaType)
    {
        var bitmap = new SKBitmap(new SKImageInfo(ink.Width, ink.Height, colorType, alphaType));
        int bpp = bitmap.Info.BytesPerPixel;
        var bytes = new byte[bitmap.RowBytes * ink.Height];
        for (int y = 0; y < ink.Height; y++)
        {
            for (int x = 0; x < ink.Width; x++)
            {
                byte v = ink.GetPixel(x, y) ? (byte)0 : (byte)255;
                int o = y * bitmap.RowBytes + x * bpp;
                switch (colorType)
                {
                    case SKColorType.Gray8:
                        bytes[o] = v;
                        break;
                    case SKColorType.Rgba8888:
                    case SKColorType.Bgra8888:
                        bytes[o] = bytes[o + 1] = bytes[o + 2] = v;
                        bytes[o + 3] = 255;
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
        }
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    [Theory]
    [InlineData(SKColorType.Gray8, SKAlphaType.Opaque)]
    [InlineData(SKColorType.Rgba8888, SKAlphaType.Opaque)]
    [InlineData(SKColorType.Rgba8888, SKAlphaType.Unpremul)]
    [InlineData(SKColorType.Rgba8888, SKAlphaType.Premul)]
    [InlineData(SKColorType.Bgra8888, SKAlphaType.Opaque)]
    [InlineData(SKColorType.Bgra8888, SKAlphaType.Premul)]
    public void CommonBitmapFormatsAreEncodedLosslessly(SKColorType colorType, SKAlphaType alphaType)
    {
        BinaryBitmap ink = TestImages.TextPage(300, 200, 2, seed: 1);
        using SKBitmap bitmap = ToSkBitmap(ink, colorType, alphaType);

        byte[] jbig2 = bitmap.EncodeJbig2ToBytes();

        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(jbig2)));
    }

    [Fact]
    public void ConvertedFormatsWorkToo()
    {
        BinaryBitmap ink = TestImages.Geometry(120, 90);
        using SKBitmap rgba = ToSkBitmap(ink, SKColorType.Rgba8888, SKAlphaType.Opaque);

        // Rgb565 and Rgb888x go through Skia's own conversion.
        foreach (SKColorType type in new[] { SKColorType.Rgb565, SKColorType.Rgb888x })
        {
            using var converted = new SKBitmap(new SKImageInfo(ink.Width, ink.Height, type, SKAlphaType.Opaque));
            using SKPixmap src = rgba.PeekPixels()!;
            Assert.True(src.ReadPixels(converted.PeekPixels()!));

            Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(converted.EncodeJbig2ToBytes())), type.ToString());
        }
    }

    [Fact]
    public void TransparentPremultipliedPixelsAreTreatedAsWhite()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(20, 10, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(3, 4, new SKColor(0, 0, 0, 255));

        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(bitmap.EncodeJbig2ToBytes());
        Assert.Equal(1, decoded.CountBlackPixels());
        Assert.True(decoded.GetPixel(3, 4));
    }

    [Fact]
    public void SKImageAndSKPixmapWorkLikeSKBitmap()
    {
        BinaryBitmap ink = TestImages.TextPage(200, 120, 2, seed: 2);
        using SKBitmap bitmap = ToSkBitmap(ink, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKPixmap pixmap = bitmap.PeekPixels()!;

        byte[] fromBitmap = bitmap.EncodeJbig2ToBytes();
        Assert.Equal(fromBitmap, image.EncodeJbig2ToBytes());
        Assert.Equal(fromBitmap, pixmap.EncodeJbig2().ToFile());
        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(fromBitmap)));
    }

    [Fact]
    public void RenderedTextRoundTripsAndLossyIsSmaller()
    {
        // Real anti-aliased text drawn by Skia at a print-like size.
        using var bitmap = new SKBitmap(new SKImageInfo(900, 500, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(SKTypeface.Default, 34);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (int line = 0; line < 9; line++)
            {
                canvas.DrawText("The quick brown fox jumps over 1234567890 lazy dogs", 20, 50 + line * 48, SKTextAlign.Left, font, paint);
            }
        }

        var options = new Jbig2ImageOptions
        {
            Binarization = new BinarizationOptions { Mode = BinarizationMode.Global },
        };
        BinaryBitmap expected = bitmap.ToJbig2Bitmap(options.Binarization);
        Assert.True(expected.CountBlackPixels() > 5000);

        byte[] lossless = bitmap.EncodeJbig2ToBytes(options);
        Assert.True(expected.PixelsEqual(Oracles.DecodeWithJBig2Decoder(lossless)));

        options.Encoding.Mode = Jbig2Mode.Lossy;
        options.Encoding.SymbolThreshold = 0.95f;
        byte[] lossy = bitmap.EncodeJbig2ToBytes(options);
        BinaryBitmap decoded = Oracles.DecodeWithJBig2Decoder(lossy);

        int differing = 0;
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (expected.GetPixel(x, y) != decoded.GetPixel(x, y)) differing++;

        Assert.True(lossy.Length < lossless.Length, $"lossy {lossy.Length} vs lossless {lossless.Length}");
        Assert.True(differing < expected.CountBlackPixels() * 0.10, $"{differing} differing pixels of {expected.CountBlackPixels()} black");
    }

    [Fact]
    public void SubsetBitmapsWithPaddedRowsAreHandled()
    {
        BinaryBitmap ink = TestImages.TextPage(300, 200, 2, seed: 9);
        using SKBitmap full = ToSkBitmap(ink, SKColorType.Rgba8888, SKAlphaType.Opaque);

        // A sub-rectangle shares the parent's pixel memory, so its rows are padded (RowBytes > Width * 4).
        var rect = new SKRectI(11, 7, 11 + 173, 7 + 121);
        using var subset = new SKBitmap();
        Assert.True(full.ExtractSubset(subset, rect));
        Assert.True(subset.RowBytes > subset.Width * subset.BytesPerPixel);

        var expected = new BinaryBitmap(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
                expected.SetPixel(x, y, ink.GetPixel(rect.Left + x, rect.Top + y));

        Assert.True(expected.PixelsEqual(Oracles.DecodeWithJBig2Decoder(subset.EncodeJbig2ToBytes())));
    }

    [Fact]
    public void SaveToStreamAndFile()
    {
        BinaryBitmap ink = TestImages.Geometry(80, 60);
        using SKBitmap bitmap = ToSkBitmap(ink, SKColorType.Gray8, SKAlphaType.Opaque);

        using var ms = new MemoryStream();
        bitmap.SaveJbig2(ms);
        Assert.Equal(bitmap.EncodeJbig2ToBytes(), ms.ToArray());

        string path = Path.Combine(Path.GetTempPath(), "jbig2enc-" + Guid.NewGuid().ToString("N") + ".jb2");
        try
        {
            bitmap.SaveJbig2(path);
            Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(File.ReadAllBytes(path))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DpiIsRecordedInPixelsPerMetre()
    {
        BinaryBitmap ink = TestImages.Geometry(60, 40);
        using SKBitmap bitmap = ToSkBitmap(ink, SKColorType.Gray8, SKAlphaType.Opaque);

        var info = Jbig2Inspect.PageInformation(bitmap.EncodeJbig2ToBytes(dpi: 300));
        Assert.Equal((60, 40, 11811u, 11811u), (info.Width, info.Height, info.XRes, info.YRes));

        var unknown = Jbig2Inspect.PageInformation(bitmap.EncodeJbig2ToBytes());
        Assert.Equal((0u, 0u), (unknown.XRes, unknown.YRes));
    }

    [Fact]
    public void PdfEmbeddedLossyExposesGlobalsAndPages()
    {
        BinaryBitmap ink = TestImages.TextPage(400, 300, 3, seed: 3);
        using SKBitmap bitmap = ToSkBitmap(ink, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var options = new Jbig2ImageOptions { Encoding = new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded } };

        Assert.Throws<InvalidOperationException>(() => bitmap.EncodeJbig2ToBytes(options));

        Jbig2Document doc = bitmap.EncodeJbig2(options);
        Assert.NotNull(doc.Globals);
        Assert.True(ink.PixelsEqual(Oracles.DecodeWithJBig2Decoder(doc.Pages.Single(), doc.Globals)));
    }
}
