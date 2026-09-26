using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

public class BinarizerTests
{
    private static byte[] GrayFrom(BinaryBitmap ink, Func<int, int, int> background, int inkValue = 30)
    {
        var gray = new byte[ink.Width * ink.Height];
        for (int y = 0; y < ink.Height; y++)
            for (int x = 0; x < ink.Width; x++)
                gray[y * ink.Width + x] = (byte)(ink.GetPixel(x, y) ? inkValue : background(x, y));
        return gray;
    }

    [Fact]
    public void GlobalThresholdIsStrictlyLessThan()
    {
        byte[] gray = { 0, 127, 128, 129, 255, 10, 200, 128 };
        var bmp = Binarizer.FromGray8(gray, 8, 1, 8, new BinarizationOptions { Mode = BinarizationMode.Global }); // threshold 128

        bool[] expected = { true, true, false, false, false, true, false, false };
        for (int x = 0; x < 8; x++) Assert.Equal(expected[x], bmp.GetPixel(x, 0));
    }

    [Fact]
    public void ExplicitThresholdOverridesTheDefault()
    {
        byte[] gray = { 99, 100, 101 };
        var bmp = Binarizer.FromGray8(gray, 3, 1, 3, new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = 101 });
        Assert.True(bmp.GetPixel(0, 0));
        Assert.True(bmp.GetPixel(1, 0));
        Assert.False(bmp.GetPixel(2, 0));
    }

    [Fact]
    public void StrideIsRespected()
    {
        // 3x2 image inside rows of 5 bytes; the padding bytes are dark and must be ignored.
        byte[] gray = { 255, 0, 255, 0, 0,
                        0, 255, 0, 0, 0 };
        var bmp = Binarizer.FromGray8(gray, 3, 2, 5, new BinarizationOptions { Mode = BinarizationMode.Global });
        Assert.Equal(3, bmp.Width);
        Assert.False(bmp.GetPixel(0, 0)); Assert.True(bmp.GetPixel(1, 0)); Assert.False(bmp.GetPixel(2, 0));
        Assert.True(bmp.GetPixel(0, 1)); Assert.False(bmp.GetPixel(1, 1)); Assert.True(bmp.GetPixel(2, 1));
    }

    [Fact]
    public void AdaptiveModeRemovesIlluminationGradientThatDefeatsAFixedThreshold()
    {
        BinaryBitmap ink = TestImages.TextPage(400, 300, scale: 2, seed: 5);
        // Paper darkens from 250 on the left to 140 on the right: a fixed threshold of 200 would turn the right
        // half of the page black.
        byte[] gray = GrayFrom(ink, (x, y) => 250 - (x * 110) / 400);

        var global = Binarizer.FromGray8(gray, 400, 300, 400, new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = 200 });
        var adaptive = Binarizer.FromGray8(gray, 400, 300, 400);

        static int Mismatches(BinaryBitmap a, BinaryBitmap b)
        {
            int n = 0;
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
            return n;
        }

        int total = 400 * 300;
        int globalErrors = Mismatches(global, ink);
        int adaptiveErrors = Mismatches(adaptive, ink);

        // The fixed threshold is wrecked by the gradient (everything darker than 200 turns black: over 40% of
        // the page is wrong)...
        Assert.True(globalErrors > total * 0.4, $"fixed threshold produced only {globalErrors} wrong pixels");

        // ...while adaptive mode recovers the ink almost exactly.
        Assert.True(adaptiveErrors < total * 0.005, $"adaptive result differs from the ink in {adaptiveErrors} pixels");
    }

    [Fact]
    public void AdaptiveModeLeavesCleanWhitePageWhite()
    {
        var gray = new byte[300 * 200];
        Array.Fill(gray, (byte)255);
        Assert.True(Binarizer.FromGray8(gray, 300, 200, 300).IsBlank());
    }

    [Fact]
    public void ImagesSmallerThanATileStillWork()
    {
        byte[] gray = { 250, 20, 250, 250, 20, 250 };
        var bmp = Binarizer.FromGray8(gray, 3, 2, 3);
        Assert.False(bmp.GetPixel(0, 0));
        Assert.True(bmp.GetPixel(1, 0));
        Assert.True(bmp.GetPixel(1, 1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void UpsamplingScalesTheBitmapAndKeepsSolidAreas(int factor)
    {
        var options = new BinarizationOptions { Mode = BinarizationMode.Global, Upsample = factor };

        var black = Binarizer.FromGray8(new byte[] { 0, 0, 0, 0 }, 2, 2, 2, options);
        Assert.Equal(2 * factor, black.Width);
        Assert.Equal(2 * factor, black.Height);
        Assert.Equal(black.Width * black.Height, black.CountBlackPixels());

        var white = Binarizer.FromGray8(new byte[] { 255, 255, 255, 255 }, 2, 2, 2, options);
        Assert.True(white.IsBlank());
    }

    [Fact]
    public void UpsamplingSmoothsAGrayEdge()
    {
        // A single mid-gray pixel between white neighbors: interpolation lets the dark side grow into the
        // sub-pixels next to it but the far side stays white.
        byte[] gray = { 255, 255, 0, 255, 255 };
        var bmp = Binarizer.FromGray8(gray, 5, 1, 5, new BinarizationOptions { Mode = BinarizationMode.Global, Upsample = 2 });
        Assert.Equal(10, bmp.Width);
        Assert.True(bmp.GetPixel(4, 0));   // the dark source pixel itself
        Assert.False(bmp.GetPixel(0, 0));
        Assert.False(bmp.GetPixel(9, 0));
    }

    [Fact]
    public void ColorLayoutsAgreeAndAlphaCompositesOverWhite()
    {
        byte[] rgb = { 0, 0, 0, 255, 255, 255, 200, 0, 0 };            // black, white, dark-ish red
        byte[] bgr = { 0, 0, 0, 255, 255, 255, 0, 0, 200 };
        byte[] rgba = { 0, 0, 0, 255, 255, 255, 255, 255, 200, 0, 0, 255 };
        byte[] transparent = { 0, 0, 0, 0 };                           // black but fully transparent

        var o = new BinarizationOptions { Mode = BinarizationMode.Global };
        var a = Binarizer.FromPixels(rgb, 3, 1, 9, PixelLayout.Rgb24, o);
        var b = Binarizer.FromPixels(bgr, 3, 1, 9, PixelLayout.Bgr24, o);
        var c = Binarizer.FromPixels(rgba, 3, 1, 12, PixelLayout.Rgba32, o);
        Assert.True(a.PixelsEqual(b));
        Assert.True(a.PixelsEqual(c));
        Assert.True(a.GetPixel(0, 0));
        Assert.False(a.GetPixel(1, 0));

        Assert.True(Binarizer.FromPixels(transparent, 1, 1, 4, PixelLayout.Rgba32, o).IsBlank());
    }

    [Fact]
    public void LumaAndGreenConversionsDiffer()
    {
        // Pure red: luma ~76 (dark), green channel 0 => both dark. Pure green: luma ~150, green 255.
        byte[] green = { 0, 255, 0 };
        var luma = Binarizer.FromPixels(green, 1, 1, 3, PixelLayout.Rgb24,
            new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = 200, GrayConversion = GrayConversion.Luma });
        var greenOnly = Binarizer.FromPixels(green, 1, 1, 3, PixelLayout.Rgb24,
            new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = 200, GrayConversion = GrayConversion.Green });
        Assert.True(luma.GetPixel(0, 0));
        Assert.False(greenOnly.GetPixel(0, 0));
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        byte[] gray = new byte[4];
        Assert.Throws<ArgumentException>(() => Binarizer.FromGray8(gray, 2, 2, 2, new BinarizationOptions { Upsample = 3 }));
        Assert.Throws<ArgumentException>(() => Binarizer.FromGray8(gray, 2, 2, 2, new BinarizationOptions { Threshold = 256 }));
        Assert.Throws<ArgumentException>(() => Binarizer.FromGray8(gray, 2, 2, 2, new BinarizationOptions { BlackValue = 100, WhiteValue = 100 }));
        Assert.Throws<ArgumentException>(() => Binarizer.FromGray8(new byte[3], 2, 2, 2));
    }
}
