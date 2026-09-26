using System.Diagnostics;
using System.Text;

namespace JBIG2Encoder.Tests.TestSupport;

/// <summary>
/// Extracts the decoded images of a PDF with poppler's <c>pdfimages</c>, which uses poppler's own JBIG2
/// decoder (independent of the encoder and of the JPedal-derived decoder used elsewhere in the tests). This
/// verifies that a real PDF reader understands the encoder's output, including the globals stream.
/// </summary>
/// <remarks>
/// <c>pdfimages</c> is used instead of <c>pdftoppm</c> on purpose: rendering resamples the image and would
/// show differences that have nothing to do with the JBIG2 data. Not available on every machine.
/// </remarks>
internal static class PopplerOracle
{
    private static readonly Lazy<string?> Executable = new(FindExecutable);

    public static bool IsAvailable => Executable.Value != null;

    private static string? FindExecutable()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("JBIG2_PDFIMAGES");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        string exe = OperatingSystem.IsWindows() ? "pdfimages.exe" : "pdfimages";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }
        return null;
    }

    /// <summary>Returns the decoded 1-bit images of the PDF in document order.</summary>
    public static List<BinaryBitmap> ExtractImages(byte[] pdf)
    {
        string exe = Executable.Value ?? throw new InvalidOperationException("pdfimages is not available.");
        string dir = Path.Combine(Path.GetTempPath(), "jbig2enc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pdfPath = Path.Combine(dir, "in.pdf");
            File.WriteAllBytes(pdfPath, pdf);

            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(pdfPath);
            psi.ArgumentList.Add(Path.Combine(dir, "img"));

            using var process = Process.Start(psi)!;
            string stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(60_000)) { process.Kill(true); throw new TimeoutException("pdfimages timed out."); }
            if (process.ExitCode != 0 || stderr.Contains("Error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"pdfimages failed (exit {process.ExitCode}): {stderr}");
            }

            return Directory.GetFiles(dir, "img-*.pbm")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => ReadPbm(File.ReadAllBytes(f)))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* best effort */ }
        }
    }

    private static BinaryBitmap ReadPbm(byte[] data)
    {
        int pos = 0;
        string ReadToken()
        {
            while (pos < data.Length && char.IsWhiteSpace((char)data[pos])) pos++;
            var sb = new StringBuilder();
            while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) sb.Append((char)data[pos++]);
            return sb.ToString();
        }

        if (ReadToken() != "P4") throw new InvalidDataException("Expected a binary PBM.");
        int w = int.Parse(ReadToken()), h = int.Parse(ReadToken());
        pos++; // single whitespace before the raster

        // PBM: rows of packed bits, 1 = black.
        return BinaryBitmap.FromPackedBits(data.AsSpan(pos), w, h, (w + 7) / 8, oneIsBlack: true);
    }
}
