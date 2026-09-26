using System.Diagnostics;
using System.Globalization;
using System.Text;
using JBIG2Encoder;
using JBIG2Encoder.SkiaSharp;
using JBIG2Encoder.Tests.TestSupport;
using SkiaSharp;

// Compares this library against the native jbig2enc executable on identical 1-bit input:
//   1. byte-for-byte equality of the encoded output, and
//   2. encoding time.
//
// Usage: ReferenceComparison <path-to-jbig2(.exe)> [--runs N] [--quick] [--bytes-only]
//
// The native tool is run as a process (start-up, PBM loading and encoding are all included, as in normal use); its
// fixed start-up cost is measured separately on a tiny image and reported so it can be subtracted. This library is
// timed in-process on an already loaded bitmap, after warm-up, and once "cold" (first call in the process, includes JIT).

return Comparison.Run(args);

internal static class Comparison
{
    private static string _native = "";
    private static string _work = "";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: ReferenceComparison <path-to-jbig2(.exe)> [--runs N] [--quick] [--bytes-only]");
            return 2;
        }
        _native = args[0];
        int runs = args.Contains("--runs") ? int.Parse(args[Array.IndexOf(args, "--runs") + 1]) : 15;
        bool quick = args.Contains("--quick");
        bool bytesOnly = args.Contains("--bytes-only");
        _work = Path.Combine(Path.GetTempPath(), "jbig2-refcmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);

        try
        {
            RunNative(new[] { "-V" }, out byte[] versionOut, out string versionErr);
            string version = (Encoding.UTF8.GetString(versionOut) + versionErr).Trim().Replace("\r", "").Replace("\n", " | ");
            Console.WriteLine($"native: {version}");
            Console.WriteLine($"runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {Environment.ProcessorCount} logical cores, " +
                              $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            Console.WriteLine();

            int failures = 0;
            failures += CompareBytesGeneric();
            failures += CompareBytesSymbol(quick);
            Console.WriteLine();
            if (!bytesOnly) Timing(runs, quick);
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(_work, true); } catch (IOException) { }
        }
    }

    #region Test pages

    /// <summary>Text drawn by Skia with a real font, binarized with a fixed threshold: diverse glyph shapes, like a print-out.</summary>
    private static BinaryBitmap RenderedText(int width, int height, int fontSize, int lineHeight)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(SKTypeface.Default, fontSize);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            string[] words = ("Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore " +
                              "et dolore magna aliqua Ut enim ad minim veniam quis nostrud exercitation ullamco laboris nisi " +
                              "ut aliquip ex ea commodo consequat 0123456789 Invoice 2026 Total 1,284.50 EUR").Split(' ');
            var rng = new Random(12);
            for (int y = lineHeight; y < height - lineHeight / 2; y += lineHeight)
            {
                float x = lineHeight;
                while (x < width - lineHeight * 6)
                {
                    string word = words[rng.Next(words.Length)];
                    canvas.DrawText(word, x, y, SKTextAlign.Left, font, paint);
                    x += font.MeasureText(word) + fontSize * 0.35f;
                }
            }
        }
        return bitmap.ToJbig2Bitmap(new BinarizationOptions { Mode = BinarizationMode.Global });
    }

    /// <summary>
    /// A page where no two things tie: every shape class has a unique size and every instance a unique baseline, so
    /// the (unspecified) order in which std::sort arranges equal keys cannot influence the output. Byte-identical
    /// output on such a page shows the two implementations code the same thing the same way.
    /// </summary>
    private static BinaryBitmap TieFreePage(int instances, int classes, int classOffset, int extraUniqueClass = -1)
    {
        int width = 40 * instances + 60;
        int height = 9 * instances + 100;
        var page = new BinaryBitmap(width, height);

        void Shape(int x, int y, int w, int h)
        {
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                    if (xx == 0 || yy == 0 || xx == w - 1 || yy == h - 1 || xx * (h - 1) == yy * (w - 1)) page.SetPixel(x + xx, y + yy, true);
        }

        for (int n = 0; n < instances; n++)
        {
            int k = (n + classOffset) % classes;
            Shape(12 + n * 40, 12 + n * 9, 9 + 4 * k, 11 + 4 * k);
        }
        if (extraUniqueClass >= 0) Shape(width - 70, height - 70, 41 + extraUniqueClass, 43); // used once on this page only
        return page;
    }

    private static BinaryBitmap WithMargin(BinaryBitmap source, int margin)
    {
        var result = new BinaryBitmap(source.Width + 2 * margin, source.Height + 2 * margin);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (source.GetPixel(x, y)) result.SetPixel(x + margin, y + margin, true);
        return result;
    }

    private static BinaryBitmap ScannerNoise(BinaryBitmap clean, double density, int seed)
    {
        BinaryBitmap b = clean.Clone();
        var rng = new Random(seed);
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (rng.NextDouble() < density) b.SetPixel(x, y, !b.GetPixel(x, y));
        return b;
    }

    #endregion

    #region Native tool

    private static string WritePbm(BinaryBitmap bmp, string name)
    {
        string path = Path.Combine(_work, name + ".pbm");
        byte[] packed = bmp.ToPackedBits(oneIsBlack: true, out _);
        using var fs = File.Create(path);
        fs.Write(Encoding.ASCII.GetBytes($"P4\n{bmp.Width} {bmp.Height}\n"));
        fs.Write(packed);
        // Leptonica refuses to recognise files of fewer than ~12 bytes ("truncated file"). PBM readers ignore
        // trailing bytes, so tiny images are padded.
        long missing = 64 - fs.Length;
        if (missing > 0) fs.Write(new byte[missing]);
        return path;
    }

    /// <summary>Runs the native tool; returns wall-clock milliseconds. Standard output is captured as bytes.</summary>
    private static double RunNative(IEnumerable<string> arguments, out byte[] stdout, out string stderr, int timeoutMs = -1)
    {
        var psi = new ProcessStartInfo(_native)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _work,
        };
        foreach (string a in arguments) psi.ArgumentList.Add(a);
        // The conda distribution keeps its DLLs next to the executable; make sure they are found.
        psi.Environment["PATH"] = Path.GetDirectoryName(Path.GetFullPath(_native)) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        using var ms = new MemoryStream();
        Task<string> err = p.StandardError.ReadToEndAsync();
        Task copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(true);
            p.WaitForExit();
            stdout = Array.Empty<byte>();
            stderr = "timed out";
            return -1;
        }
        copy.Wait();
        sw.Stop();
        stdout = ms.ToArray();
        stderr = err.Result;
        return sw.Elapsed.TotalMilliseconds;
    }

    private static string RunNativeOrThrow(IEnumerable<string> arguments, out byte[] stdout, out double ms)
    {
        ms = RunNative(arguments, out stdout, out string err);
        return err;
    }

    #endregion

    #region Byte-for-byte comparison

    private static int _checks;

    private static int Check(string label, byte[] native, byte[] ours)
    {
        _checks++;
        if (native.AsSpan().SequenceEqual(ours))
        {
            Console.WriteLine($"  identical   {label,-58} {ours.Length,9:N0} bytes");
            return 0;
        }

        int firstDiff = 0;
        int n = Math.Min(native.Length, ours.Length);
        while (firstDiff < n && native[firstDiff] == ours[firstDiff]) firstDiff++;
        Console.WriteLine($"  DIFFERENT   {label,-58} native {native.Length:N0} vs ours {ours.Length:N0} bytes, first difference at offset {firstDiff}");
        return 1;
    }

    /// <summary>Generic-region (lossless) output must be identical: the coder is deterministic.</summary>
    private static int CompareBytesGeneric()
    {
        Console.WriteLine("Generic region (lossless): native vs this library");
        int failures = 0;

        var cases = new List<(string Name, BinaryBitmap Image)>();
        foreach ((int w, int h) in new[] { (1, 1), (7, 3), (31, 5), (32, 5), (33, 5), (64, 64), (100, 37), (257, 129), (1000, 300) })
        {
            cases.Add(($"noise50 {w}x{h}", TestImages.Noise(w, h, 0.5, 1)));
            cases.Add(($"noise5 {w}x{h}", TestImages.Noise(w, h, 0.05, 2)));
            cases.Add(($"geometry {w}x{h}", TestImages.Geometry(w, h)));
            cases.Add(($"blank {w}x{h}", TestImages.Blank(w, h)));
            cases.Add(($"solid {w}x{h}", TestImages.Solid(w, h)));
        }
        cases.Add(("rendered text 1240x1754", RenderedText(1240, 1754, 22, 34)));
        cases.Add(("rendered text 2480x3508 (A4 @300dpi)", RenderedText(2480, 3508, 44, 68)));

        int index = 0;
        int shownIdentical = 0;
        foreach ((string name, BinaryBitmap image) in cases)
        {
            string pbm = WritePbm(image, "g" + index++);
            foreach ((string flags, Jbig2Options options) in new[]
            {
                ("", new Jbig2Options()),
                ("-d", new Jbig2Options { DuplicateLineRemoval = true }),
                ("-p", new Jbig2Options { Output = Jbig2Output.PdfEmbedded }),
                ("-p -d", new Jbig2Options { Output = Jbig2Output.PdfEmbedded, DuplicateLineRemoval = true }),
            })
            {
                var args = flags.Length == 0 ? new[] { pbm } : flags.Split(' ').Append(pbm).ToArray();
                RunNativeOrThrow(args, out byte[] native, out _);
                byte[] ours = Jbig2Encoder.EncodeGenericRegion(image, options);

                // Print every difference, but keep the successful output compact.
                bool same = native.AsSpan().SequenceEqual(ours);
                _checks++;
                if (!same)
                {
                    failures++;
                    int d = 0;
                    while (d < Math.Min(native.Length, ours.Length) && native[d] == ours[d]) d++;
                    Console.WriteLine($"  DIFFERENT   {name} [{(flags.Length == 0 ? "default" : flags)}]: native {native.Length} vs ours {ours.Length} bytes, first difference at offset {d}");
                }
                else if (name.StartsWith("rendered", StringComparison.Ordinal) || (flags.Length == 0 && shownIdentical++ < 6))
                {
                    Console.WriteLine($"  identical   {name + " [" + (flags.Length == 0 ? "default" : flags) + "]",-58} {ours.Length,9:N0} bytes");
                }
            }
        }
        Console.WriteLine($"  -> {_checks - failures} of {_checks} generic-region outputs are byte-identical" + (failures == 0 ? "" : $" ({failures} differ)"));
        return failures;
    }

    private sealed record SymbolScenario(string Name, List<BinaryBitmap> Pages, float Threshold = 0.92f, bool Auto = false, bool Standalone = false);

    /// <summary>
    /// Symbol-mode (lossy) output. Two groups:
    /// <list type="bullet">
    /// <item>pages without ties (unique sizes and baselines), where identical bytes are expected and required;</item>
    /// <item>realistic pages, where equal sort keys (which std::sort orders arbitrarily) can legitimately reorder symbols;
    /// there the decoded pictures are compared instead.</item>
    /// </list>
    /// </summary>
    private static int CompareBytesSymbol(bool quick)
    {
        Console.WriteLine();
        Console.WriteLine("Symbol mode (lossy): native vs this library");
        int failures = 0;

        Console.WriteLine("  Pages without ties (unique shape sizes and baselines) - identical bytes required:");
        var tieFree = new[]
        {
            new SymbolScenario("1 page, 30 instances of 6 shapes, PDF streams", new() { TieFreePage(30, 6, 0) }),
            new SymbolScenario("3 pages, shared shapes + one page-only shape, PDF streams",
                new() { TieFreePage(24, 5, 0), TieFreePage(20, 5, 2, extraUniqueClass: 0), TieFreePage(26, 5, 1) }),
            new SymbolScenario("3 pages, standalone file (native lacks the end-of-file segment)",
                new() { TieFreePage(24, 5, 0), TieFreePage(20, 5, 2, extraUniqueClass: 0), TieFreePage(26, 5, 1) }, Standalone: true),
            new SymbolScenario("1 page, threshold 0.97 + auto threshold", new() { TieFreePage(30, 6, 1) }, 0.97f, true),
        };
        int id = 0;
        foreach (SymbolScenario sc in tieFree) failures += RunSymbolScenario(++id, sc, mustBeIdentical: true);

        Console.WriteLine("  Realistic pages (equal sort keys occur; native and managed pictures compared) - not required to be byte-identical:");
        var glyphGrid = TestImages.TextPage(640, 480, 3, 2);
        var realistic = new List<SymbolScenario>
        {
            new("crisp glyph grid, 12 px margin", new() { WithMargin(glyphGrid, 12) }),
            new("crisp glyph grid touching the top/left edge", new() { glyphGrid }),
            new("crisp glyph grid, 3 pages, 12 px margin", new() { WithMargin(TestImages.TextPage(500, 400, 3, 10), 12), WithMargin(TestImages.TextPage(500, 400, 3, 11), 12), WithMargin(TestImages.TextPage(420, 300, 3, 12), 12) }),
            new("rendered text 1240x1754", new() { RenderedText(1240, 1754, 22, 34) }),
            new("rendered text + scanner noise", new() { ScannerNoise(RenderedText(1240, 1754, 22, 34), 0.002, 4) }),
            new("rendered text + noise, threshold 0.97", new() { ScannerNoise(RenderedText(1240, 1754, 22, 34), 0.002, 4) }, 0.97f),
            new("rendered text + noise, auto threshold (hash)", new() { ScannerNoise(RenderedText(1240, 1754, 22, 34), 0.002, 4) }, 0.92f, true),
            new("rendered text, 3 pages", new() { RenderedText(1240, 900, 22, 34), RenderedText(1240, 900, 24, 36), RenderedText(1240, 900, 22, 34) }),
        };
        if (!quick) realistic.Add(new("rendered text A4 @300dpi", new() { RenderedText(2480, 3508, 44, 68) }));
        foreach (SymbolScenario sc in realistic) RunSymbolScenario(++id, sc, mustBeIdentical: false);

        return failures;
    }

    private static int RunSymbolScenario(int id, SymbolScenario sc, bool mustBeIdentical)
    {
        var args = new List<string> { "-s", "-t", sc.Threshold.ToString(CultureInfo.InvariantCulture) };
        if (!sc.Standalone) args.AddRange(new[] { "-p", "-b", "s" + id });
        if (sc.Auto) args.Add("-a");
        for (int i = 0; i < sc.Pages.Count; i++) args.Add(WritePbm(sc.Pages[i], $"s{id}_{i}"));
        RunNativeOrThrow(args, out byte[] nativeStdout, out _);

        var encoder = new Jbig2DocumentEncoder(new Jbig2Options
        {
            Mode = Jbig2Mode.Lossy,
            Output = sc.Standalone ? Jbig2Output.StandaloneFile : Jbig2Output.PdfEmbedded,
            SymbolThreshold = sc.Threshold,
            AutoThreshold = sc.Auto,
        });
        foreach (BinaryBitmap p in sc.Pages) encoder.AddPage(p);
        Jbig2Document ours = encoder.Finish();

        bool identical;
        long nativeTotal, oursTotal;
        byte[]? nativeGlobals = null;
        var nativePages = new List<byte[]>();
        if (sc.Standalone)
        {
            // The reference never writes the end-of-file segment (an 11-byte segment header, see the README); this library does.
            byte[] file = ours.ToFile();
            identical = file.AsSpan(0, file.Length - 11).SequenceEqual(nativeStdout);
            nativeTotal = nativeStdout.Length;
            oursTotal = file.Length - 11;
        }
        else
        {
            string sym = Path.Combine(_work, $"s{id}.sym");
            nativeGlobals = File.Exists(sym) ? File.ReadAllBytes(sym) : null;
            identical = (nativeGlobals ?? Array.Empty<byte>()).AsSpan().SequenceEqual(ours.Globals ?? Array.Empty<byte>());
            nativeTotal = nativeGlobals?.Length ?? 0;
            oursTotal = ours.Globals?.Length ?? 0;
            for (int i = 0; i < sc.Pages.Count; i++)
            {
                byte[] n = File.ReadAllBytes(Path.Combine(_work, $"s{id}.{i:D4}"));
                nativePages.Add(n);
                identical &= n.AsSpan().SequenceEqual(ours.Pages[i]);
                nativeTotal += n.Length;
                oursTotal += ours.Pages[i].Length;
            }
        }

        _checks++;
        if (identical)
        {
            Console.WriteLine($"    identical   {sc.Name,-64} {oursTotal,9:N0} bytes");
            return 0;
        }

        // Not identical: compare what a reader would see. (A standalone multi-page file cannot be decoded page by page
        // by the decoder used here, so this is only done for the PDF-style streams.)
        string picture = "n/a";
        if (!sc.Standalone)
        {
            int differing = 0;
            long total = 0, nativeVsSource = 0, oursVsSource = 0, nearEdge = 0;
            try
            {
                for (int i = 0; i < sc.Pages.Count; i++)
                {
                    BinaryBitmap dn = Oracles.DecodeWithJBig2Decoder(nativePages[i], nativeGlobals);
                    BinaryBitmap dm = Oracles.DecodeWithJBig2Decoder(ours.Pages[i], ours.Globals);
                    BinaryBitmap src = sc.Pages[i];
                    for (int y = 0; y < dn.Height; y++)
                        for (int x = 0; x < dn.Width; x++)
                        {
                            total++;
                            if (dn.GetPixel(x, y) != dm.GetPixel(x, y))
                            {
                                differing++;
                                if (x < 12 || y < 12) nearEdge++; // Leptonica's alignment quirk affects the top and left edges
                            }
                            if (dn.GetPixel(x, y) != src.GetPixel(x, y)) nativeVsSource++;
                            if (dm.GetPixel(x, y) != src.GetPixel(x, y)) oursVsSource++;
                        }
                }
                picture = differing == 0
                    ? "decoded pictures identical"
                    : $"pictures differ in {differing:N0} of {total:N0} px ({nearEdge:N0} of them within 12 px of the top/left edge; native vs source {nativeVsSource:N0}, ours vs source {oursVsSource:N0})";
            }
            catch (Exception e)
            {
                picture = "decode failed: " + e.Message;
            }
        }
        Console.WriteLine($"    {(mustBeIdentical ? "DIFFERENT  " : "different  ")} {sc.Name,-64} native {nativeTotal:N0} vs ours {oursTotal:N0} bytes; {picture}");
        return mustBeIdentical ? 1 : 0;
    }

    #endregion

    #region Timing

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static void Timing(int runs, bool quick)
    {
        Console.WriteLine("Timing (median of runs; lower is better)");

        // Fixed cost of starting the native tool: a tiny image.
        string tiny = WritePbm(TestImages.Noise(8, 8, 0.5, 1), "tiny");
        var startup = new List<double>();
        for (int i = 0; i < 12; i++) startup.Add(RunNative(new[] { tiny }, out _, out _));
        double startupMs = Median(startup);
        Console.WriteLine($"  native process start-up + tiny image: {startupMs:F1} ms (fixed cost included in every native number below)");

        var pages = new List<(string Name, BinaryBitmap Image)>
        {
            ("rendered text, A4 @300dpi (2480x3508)", RenderedText(2480, 3508, 44, 68)),
            ("rendered text + scanner noise, A4 @300dpi", ScannerNoise(RenderedText(2480, 3508, 44, 68), 0.002, 4)),
            ("rendered text, A4 @600dpi (4960x7016)", RenderedText(4960, 7016, 88, 136)),
            ("dense noise 30%, 2000x2000 (worst case)", TestImages.Noise(2000, 2000, 0.30, 5)),
        };
        if (quick) pages.RemoveAt(2);

        // Cold: the very first encode in this process, including JIT compilation.
        {
            BinaryBitmap small = pages[0].Image;
            var sw = Stopwatch.StartNew();
            _ = Jbig2Encoder.EncodeGenericRegion(small);
            sw.Stop();
            Console.WriteLine($"  this library, first call in a new process (cold, includes JIT), A4 @300dpi generic: {sw.Elapsed.TotalMilliseconds:F1} ms");
        }
        Console.WriteLine();

        Console.WriteLine($"  {"page / mode",-66} {"native total",13} {"native-startup",15} {"this library",13} {"ratio*",8}");
        int index = 0;
        foreach ((string name, BinaryBitmap image) in pages)
        {
            string pbm = WritePbm(image, "t" + index++);

            TimeOne($"{name} — lossless", image, runs, startupMs,
                native: () => RunNative(new[] { pbm }, out _, out _),
                ours: () => Jbig2Encoder.EncodeGenericRegion(image));

            TimeOne($"{name} — lossless, TPGD (-d)", image, runs, startupMs,
                native: () => RunNative(new[] { "-d", pbm }, out _, out _),
                ours: () => Jbig2Encoder.EncodeGenericRegion(image, new Jbig2Options { DuplicateLineRemoval = true }));

            if (name.StartsWith("dense", StringComparison.Ordinal)) continue; // lossy: see the single-run measurement below

            TimeOne($"{name} — lossy (-s)", image, runs, startupMs,
                native: () => RunNative(new[] { "-s", "-p", "-b", "tt", pbm }, out _, out _),
                ours: () => Jbig2Encoder.Encode(image, new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded }));
        }
        Console.WriteLine();
        Console.WriteLine("  * ratio = (native total) / (this library); above 1 means this library is faster. 'native-startup' subtracts the fixed");
        Console.WriteLine("    process start-up cost measured above, which is the fairer comparison for a long-running application.");

        // A pathological input for symbol coding: random noise is not text, it produces hundreds of thousands of tiny,
        // mostly different components. One run each; the native tool is given at most 10 minutes.
        BinaryBitmap noise = TestImages.Noise(2000, 2000, 0.30, 5);
        string noisePbm = WritePbm(noise, "noise");
        Console.WriteLine();
        Console.WriteLine("  Pathological input, single run each: dense random noise 30 %, 2000x2000, lossy");
        var sw2 = Stopwatch.StartNew();
        _ = Jbig2Encoder.Encode(noise, new Jbig2Options { Mode = Jbig2Mode.Lossy, Output = Jbig2Output.PdfEmbedded });
        sw2.Stop();
        double nativeNoiseMs = RunNative(new[] { "-s", "-p", "-b", "tn", noisePbm }, out _, out _, timeoutMs: 600_000);
        Console.WriteLine($"    native: {(nativeNoiseMs < 0 ? "> 600000 ms (stopped)" : $"{nativeNoiseMs:F0} ms")}   this library: {sw2.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static void TimeOne(string label, BinaryBitmap image, int runs, double startupMs, Func<double> native, Func<object> ours)
    {
        // Native: a few runs (each is a process), first one discarded to warm the file cache.
        native();
        var nativeTimes = new List<double>();
        for (int i = 0; i < Math.Max(5, runs / 3); i++) nativeTimes.Add(native());
        double nativeMs = Median(nativeTimes);

        // Ours: warm up, then measure.
        for (int i = 0; i < 3; i++) ours();
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            ours();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        double oursMs = Median(times);

        double adjusted = Math.Max(nativeMs - startupMs, 0.1);
        Console.WriteLine($"  {label,-66} {nativeMs,10:F1} ms {adjusted,12:F1} ms {oursMs,10:F1} ms {nativeMs / oursMs,7:F1}x  ({adjusted / oursMs:F1}x vs startup-adjusted)");
    }

    #endregion
}
