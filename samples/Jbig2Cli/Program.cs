using JBIG2Encoder;
using JBIG2Encoder.SkiaSharp;
using SkiaSharp;

// A small command line front end modelled on the flags of the original jbig2enc tool, built on the SkiaSharp
// integration. Skia decodes PNG, JPEG, WebP, BMP and GIF input.

return Jbig2Cli.Run(args);

internal static class Jbig2Cli
{
    private const string Usage = """
        jbig2cli - encode images as JBIG2

        Usage: jbig2cli [options] image [image...]

          -o <file>       write the result to a file (default: standard output)
          -s              lossy symbol mode (default: lossless generic region; encodes ONE image)
          -t <0.4-0.98>   symbol classification threshold (default 0.92)
          -w <0-1>        symbol classification weight (default 0.5)
          -a              merge symbols that compare equal (auto threshold); --no-hash compares all pairs
          -d              duplicate line removal in generic mode (faster, very slightly larger)
          -p              PDF-embedded streams instead of a standalone file
          -b <basename>   with -p: write <basename>.sym (globals) and <basename>.0000, .0001, ... (pages)
          -T <0-255>      black/white threshold (default 200, or 128 with -G)
          -G              global threshold instead of background cleaning
          -2 | -4         upsample 2x / 4x before thresholding
          -D <dpi>        resolution to record
          -v              verbose
        """;

    public static int Run(string[] args)
    {
        var options = new Jbig2ImageOptions();
        var encoding = options.Encoding;
        var binarization = options.Binarization;
        string? outputFile = null;
        string basename = "output";
        bool verbose = false;
        var inputs = new List<string>();

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h" or "--help":
                        Console.Error.WriteLine(Usage);
                        return 0;
                    case "-o": outputFile = Next(args, ref i); break;
                    case "-s": encoding.Mode = Jbig2Mode.Lossy; break;
                    case "-t": encoding.SymbolThreshold = float.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "-w": encoding.SymbolWeight = float.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "-a": encoding.AutoThreshold = true; break;
                    case "--no-hash": encoding.AutoThresholdUsesHash = false; break;
                    case "-d": encoding.DuplicateLineRemoval = true; break;
                    case "-p": encoding.Output = Jbig2Output.PdfEmbedded; break;
                    case "-b": basename = Next(args, ref i); break;
                    case "-T": binarization.Threshold = int.Parse(Next(args, ref i)); break;
                    case "-G": binarization.Mode = BinarizationMode.Global; break;
                    case "-2": binarization.Upsample = 2; break;
                    case "-4": binarization.Upsample = 4; break;
                    case "-D": encoding.Dpi = int.Parse(Next(args, ref i)); break;
                    case "-v": verbose = true; break;
                    default:
                        if (args[i].StartsWith('-') && args[i].Length > 1) throw new ArgumentException($"Unknown option {args[i]}");
                        inputs.Add(args[i]);
                        break;
                }
            }

            if (inputs.Count == 0)
            {
                Console.Error.WriteLine(Usage);
                return 1;
            }
            if (encoding.Mode == Jbig2Mode.Lossless && inputs.Count > 1)
            {
                throw new ArgumentException("Generic (lossless) mode encodes a single image; use -s for several pages.");
            }

            var encoder = new Jbig2DocumentEncoder(encoding);
            Jbig2Document document;
            if (encoding.Mode == Jbig2Mode.Lossless)
            {
                document = Jbig2Encoder.Encode(Load(inputs[0], options, verbose), encoding);
            }
            else
            {
                foreach (string input in inputs) encoder.AddPage(Load(input, options, verbose));
                document = encoder.Finish();
            }

            if (encoding.Output == Jbig2Output.PdfEmbedded && (encoding.Mode == Jbig2Mode.Lossy || outputFile == null))
            {
                // Separate streams: globals and one file per page, like the original tool's -p mode.
                if (document.Globals != null) File.WriteAllBytes(basename + ".sym", document.Globals);
                for (int i = 0; i < document.Pages.Count; i++) File.WriteAllBytes($"{basename}.{i:D4}", document.Pages[i]);
                if (verbose) Console.Error.WriteLine($"wrote {(document.Globals != null ? basename + ".sym + " : "")}{document.Pages.Count} page file(s)");
                return 0;
            }

            byte[] data = document.ToSingleStream();
            if (outputFile != null) File.WriteAllBytes(outputFile, data);
            else
            {
                using Stream stdout = Console.OpenStandardOutput();
                stdout.Write(data, 0, data.Length);
            }
            if (verbose) Console.Error.WriteLine($"wrote {data.Length} bytes");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 2;
        }
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing argument for {args[i]}");
        return args[++i];
    }

    private static BinaryBitmap Load(string path, Jbig2ImageOptions options, bool verbose)
    {
        using SKBitmap bitmap = SKBitmap.Decode(path)
            ?? throw new IOException($"Cannot decode image \"{path}\".");
        if (verbose) Console.Error.WriteLine($"{path}: {bitmap.Width}x{bitmap.Height} {bitmap.ColorType}");
        return bitmap.ToJbig2Bitmap(options.Binarization);
    }
}
