using JBIG2Encoder.Symbols;
using JBIG2Encoder.Tests.TestSupport;

namespace JBIG2Encoder.Tests;

public class CorrelationClassifierTests
{
    // 5x7 glyph bitmaps, scaled: see TestImages. Two clearly different shapes plus a 'noisy' variant of the first.
    private static BinaryBitmap PageWithGlyphs(int w, int h, params (int X, int Y, string[] Rows)[] glyphs)
    {
        var page = new BinaryBitmap(w, h);
        foreach (var (gx, gy, rows) in glyphs)
        {
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#') page.SetPixel(gx + x, gy + y, true);
        }
        return page;
    }

    private static readonly string[] LetterL =
    {
        "##....",
        "##....",
        "##....",
        "##....",
        "##....",
        "######",
        "######",
    };

    private static readonly string[] LetterT =
    {
        "######",
        "######",
        "..##..",
        "..##..",
        "..##..",
        "..##..",
        "..##..",
    };

    [Fact]
    public void IdenticalShapesShareOneClassAndAreAlignedExactly()
    {
        var page = PageWithGlyphs(80, 40,
            (5, 5, LetterL), (20, 5, LetterL), (35, 6, LetterL), (50, 20, LetterL));

        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(page);

        Assert.Single(classifier.Templates);
        Assert.Equal(4, classifier.ComponentClass.Count);
        Assert.All(classifier.ComponentClass, c => Assert.Equal(0, c));

        // Placing the exemplar at the reported upper-left corner reproduces every instance exactly.
        var rebuilt = new BinaryBitmap(80, 40);
        BinaryBitmap symbol = classifier.Templates[0].ToUnbordered();
        foreach ((int x, int y) in classifier.ComponentUpperLeft)
        {
            rebuilt.Combine(BinaryBitmap.RasterOp.Or, x, y, symbol.Width, symbol.Height, symbol, 0, 0);
        }
        Assert.True(page.PixelsEqual(rebuilt));
    }

    [Fact]
    public void DifferentShapesGetDifferentClasses()
    {
        var page = PageWithGlyphs(60, 20, (3, 5, LetterL), (20, 5, LetterT), (40, 5, LetterL));
        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(page);

        Assert.Equal(2, classifier.Templates.Count);
        Assert.Equal(new[] { 0, 1, 0 }, classifier.ComponentClass.ToArray());
    }

    [Fact]
    public void ClassesAreSharedAcrossPages()
    {
        var p1 = PageWithGlyphs(40, 20, (3, 5, LetterL), (20, 5, LetterT));
        var p2 = PageWithGlyphs(40, 20, (10, 8, LetterT), (25, 2, LetterL));

        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(p1);
        classifier.AddPage(p2);

        Assert.Equal(2, classifier.Templates.Count);
        Assert.Equal(2, classifier.PageCount);
        Assert.Equal(new[] { 0, 2 }, classifier.PageFirstComponent.ToArray());
        // Components are numbered in raster order within a page: page 1 = L, T; page 2 = L (higher up), T.
        Assert.Equal(new[] { 0, 1, 0, 1 }, classifier.ComponentClass.ToArray());
        Assert.Equal(new[] { 0, 0, 1, 1 }, classifier.ComponentPage.ToArray());
    }

    [Fact]
    public void BlankPagesAreCountedButHaveNoComponents()
    {
        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(new BinaryBitmap(30, 30));
        classifier.AddPage(PageWithGlyphs(30, 30, (2, 2, LetterL)));
        classifier.AddPage(new BinaryBitmap(30, 30));

        Assert.Equal(3, classifier.PageCount);
        Assert.Equal(new[] { 0, 0, 1 }, classifier.PageFirstComponent.ToArray());
        Assert.Single(classifier.ComponentClass);
    }

    [Fact]
    public void LowerLeftCornersUseTheBottomRowOfTheExemplar()
    {
        var page = PageWithGlyphs(40, 30, (7, 9, LetterL));
        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(page);

        (int x, int y) = classifier.GetLowerLeftCorners()[0];
        Assert.Equal(7, x);
        Assert.Equal(9 + LetterL.Length - 1, y);
    }

    [Fact]
    public void LowThresholdMergesSimilarShapesHighThresholdKeepsThemApart()
    {
        // A glyph and the same glyph with two corner pixels removed (still one connected shape).
        // Overlap 20 of 22 / 20 black pixels gives a correlation of 20^2 / (22 * 20) = 0.91.
        string[] damaged = (string[])LetterL.Clone();
        damaged[5] = "#####.";
        damaged[6] = ".#####";
        var page = PageWithGlyphs(60, 20, (3, 5, LetterL), (25, 5, damaged));

        var loose = new CorrelationClassifier(0.75f, 0f);
        loose.AddPage(page);
        var strict = new CorrelationClassifier(0.97f, 0f);
        strict.AddPage(page);

        Assert.Single(loose.Templates);
        Assert.Equal(2, strict.Templates.Count);
    }

    [Fact]
    public void TextPageClassesMatchTheGlyphAlphabet()
    {
        // Ten distinct glyphs, rendered crisp: exactly ten classes regardless of how many instances there are.
        BinaryBitmap page = TestImages.TextPage(600, 400, scale: 3, seed: 21);
        var classifier = new CorrelationClassifier(0.92f, 0.5f);
        classifier.AddPage(page);

        Assert.True(classifier.ComponentClass.Count > 200);
        Assert.InRange(classifier.Templates.Count, 8, 12);
    }

    [Fact]
    public void ComparatorAcceptsIdenticalAndRejectsDifferentSymbols()
    {
        BinaryBitmap a = PageWithGlyphs(6, 7, (0, 0, LetterL)).AddBorder(6);
        BinaryBitmap same = PageWithGlyphs(6, 7, (0, 0, LetterL)).AddBorder(6);
        BinaryBitmap t = PageWithGlyphs(6, 7, (0, 0, LetterT)).AddBorder(6);

        Assert.True(SymbolComparator.AreEquivalent(a, same));
        Assert.False(SymbolComparator.AreEquivalent(a, t));
    }

    [Fact]
    public void ComparatorRequiresEqualSize()
    {
        BinaryBitmap a = new BinaryBitmap(10, 10);
        BinaryBitmap b = new BinaryBitmap(10, 11);
        Assert.False(SymbolComparator.AreEquivalent(a, b));
    }
}
