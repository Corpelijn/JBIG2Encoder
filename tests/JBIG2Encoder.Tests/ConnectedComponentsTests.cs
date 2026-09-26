using JBIG2Encoder.Symbols;
using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

public class ConnectedComponentsTests
{
    private static BinaryBitmap FromRows(params string[] rows)
    {
        var bmp = new BinaryBitmap(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#') bmp.SetPixel(x, y, true);
        return bmp;
    }

    [Fact]
    public void FindsComponentsInRasterOrderWithTheirOwnPixelsOnly()
    {
        var page = FromRows(
            "##....#.",
            "#.....#.",
            "..##..#.",
            "..##....",
            "......##");

        List<Component> comps = ConnectedComponents.Extract(page);

        Assert.Equal(4, comps.Count);
        // Raster order of each component's first pixel: (0,0), (6,0), (2,2), (6,4).
        Assert.Equal((0, 0), (comps[0].X, comps[0].Y));
        Assert.Equal((6, 0), (comps[1].X, comps[1].Y));
        Assert.Equal((2, 2), (comps[2].X, comps[2].Y));
        Assert.Equal((6, 4), (comps[3].X, comps[3].Y));

        Assert.Equal((2, 2), (comps[0].Bitmap.Width, comps[0].Bitmap.Height));
        Assert.Equal(3, comps[0].Bitmap.CountBlackPixels());
        Assert.False(comps[0].Bitmap.GetPixel(1, 1)); // the bounding box holds no foreign pixels
        Assert.Equal((1, 3), (comps[1].Bitmap.Width, comps[1].Bitmap.Height));
        Assert.Equal((2, 2), (comps[2].Bitmap.Width, comps[2].Bitmap.Height));
        Assert.Equal(4, comps[2].Bitmap.CountBlackPixels());
    }

    [Fact]
    public void DiagonalNeighborsJoinOnlyWithEightConnectivity()
    {
        var page = FromRows(
            "#..",
            ".#.",
            "..#");

        Assert.Equal(1, ConnectedComponents.Count(page, eightConnected: true));
        Assert.Equal(3, ConnectedComponents.Count(page, eightConnected: false));
        Assert.Single(ConnectedComponents.Extract(page));
    }

    [Fact]
    public void NestedShapesAreSeparateComponents()
    {
        var page = FromRows(
            "#######",
            "#.....#",
            "#.###.#",
            "#.###.#",
            "#.....#",
            "#######");
        List<Component> comps = ConnectedComponents.Extract(page);
        Assert.Equal(2, comps.Count);
        Assert.Equal((7, 6), (comps[0].Bitmap.Width, comps[0].Bitmap.Height));
        Assert.Equal((3, 2), (comps[1].Bitmap.Width, comps[1].Bitmap.Height));
    }

    [Theory]
    [InlineData(33, 29, 0.10, 1)]
    [InlineData(200, 120, 0.30, 2)]
    [InlineData(128, 64, 0.55, 3)]
    [InlineData(64, 64, 0.02, 4)]
    public void ComponentsPartitionThePage(int w, int h, double density, int seed)
    {
        BinaryBitmap page = TestImages.Noise(w, h, density, seed);
        List<Component> comps = ConnectedComponents.Extract(page);

        var rebuilt = new BinaryBitmap(w, h);
        int totalPixels = 0;
        foreach (Component c in comps)
        {
            totalPixels += c.Bitmap.CountBlackPixels();
            rebuilt.Combine(BinaryBitmap.RasterOp.Or, c.X, c.Y, c.Bitmap.Width, c.Bitmap.Height, c.Bitmap, 0, 0);
            // Every bounding box is tight.
            Assert.True(RowHasInk(c.Bitmap, 0) && RowHasInk(c.Bitmap, c.Bitmap.Height - 1));
        }

        Assert.Equal(page.CountBlackPixels(), totalPixels); // no pixel in two components, none lost
        Assert.True(page.PixelsEqual(rebuilt));

        // Raster order: first pixels are non-decreasing in (y, x).
        (int y, int x)? previous = null;
        foreach (Component c in comps)
        {
            int fx = 0;
            while (!c.Bitmap.GetPixel(fx, 0)) fx++;
            (int y, int x) first = (c.Y, c.X + fx);
            if (previous != null) Assert.True(first.CompareTo(previous.Value) > 0);
            previous = first;
        }
    }

    private static bool RowHasInk(BinaryBitmap b, int y)
    {
        for (int x = 0; x < b.Width; x++) if (b.GetPixel(x, y)) return true;
        return false;
    }

    [Fact]
    public void BlankPageHasNoComponents()
    {
        Assert.Empty(ConnectedComponents.Extract(new BinaryBitmap(50, 50)));
    }

    [Fact]
    public void ThinFullWidthLineIsOneComponent()
    {
        var page = new BinaryBitmap(300, 5);
        for (int x = 0; x < 300; x++) page.SetPixel(x, 2, true);
        List<Component> comps = ConnectedComponents.Extract(page);
        Assert.Single(comps);
        Assert.Equal((300, 1), (comps[0].Bitmap.Width, comps[0].Bitmap.Height));
    }
}
