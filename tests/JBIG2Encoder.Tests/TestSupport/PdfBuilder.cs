using System.Globalization;
using System.Text;

namespace JBIG2Encoder.Tests.TestSupport;

/// <summary>
/// Builds a minimal PDF whose pages each show one JBIG2-encoded image at 1 pixel = 1 point, the way a
/// real PDF producer would embed the encoder's output (a <c>/JBIG2Decode</c> stream plus optional globals).
/// </summary>
internal static class PdfBuilder
{
    public sealed record Page(int Width, int Height, byte[] Jbig2Stream);

    public static byte[] Build(IReadOnlyList<Page> pages, byte[]? globals = null)
    {
        var ms = new MemoryStream();
        var offsets = new SortedDictionary<int, long>();

        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        void BeginObject(int n) { offsets[n] = ms.Position; Write($"{n} 0 obj\n"); }
        void EndObject() => Write("endobj\n");

        Write("%PDF-1.4\n%âãÏÓ\n");

        int globalsObject = 3 + 3 * pages.Count;

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObject();

        BeginObject(2);
        Write("<< /Type /Pages /Count " + pages.Count + " /Kids [" +
              string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + 3 * i} 0 R")) + "] >>\n");
        EndObject();

        for (int i = 0; i < pages.Count; i++)
        {
            Page p = pages[i];
            int pageObj = 3 + 3 * i, contentObj = pageObj + 1, imageObj = pageObj + 2;

            BeginObject(pageObj);
            Write(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] /Contents {2} 0 R /Resources << /XObject << /Im0 {3} 0 R >> >> >>\n",
                p.Width, p.Height, contentObj, imageObj));
            EndObject();

            string content = string.Format(CultureInfo.InvariantCulture, "q {0} 0 0 {1} 0 0 cm /Im0 Do Q\n", p.Width, p.Height);
            BeginObject(contentObj);
            Write($"<< /Length {content.Length} >>\nstream\n{content}endstream\n");
            EndObject();

            BeginObject(imageObj);
            string parms = globals != null ? $" /DecodeParms << /JBIG2Globals {globalsObject} 0 R >>" : "";
            Write($"<< /Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceGray " +
                  $"/BitsPerComponent 1 /Filter /JBIG2Decode{parms} /Length {p.Jbig2Stream.Length} >>\nstream\n");
            ms.Write(p.Jbig2Stream);
            Write("\nendstream\n");
            EndObject();
        }

        if (globals != null)
        {
            BeginObject(globalsObject);
            Write($"<< /Length {globals.Length} >>\nstream\n");
            ms.Write(globals);
            Write("\nendstream\n");
            EndObject();
        }

        int objectCount = offsets.Count + 1;
        long xref = ms.Position;
        Write($"xref\n0 {objectCount}\n0000000000 65535 f \n");
        foreach (var kv in offsets) Write($"{kv.Value:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objectCount} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return ms.ToArray();
    }
}
