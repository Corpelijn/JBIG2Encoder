using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace JBIG2Encoder.ImageSharp;

/// <summary>
/// Encodes an ImageSharp image as a JBIG2 stream. Because JBIG2 is a bi-level format the image is first converted to
/// black and white (see <see cref="Binarization"/>).
/// </summary>
/// <remarks>
/// Only a single stream can be written here, so lossy (symbol) output uses
/// <see cref="Jbig2Output.StandaloneFile"/>, and <see cref="Jbig2Output.PdfEmbedded"/> is limited to lossless output.
/// To obtain the separate globals and page streams that PDF wants, use
/// <see cref="ImageSharpJbig2Extensions.ToJbig2Bitmap{TPixel}(Image{TPixel}, BinarizationOptions?)"/> with
/// <see cref="Jbig2Encoder.Encode"/> or <see cref="Jbig2DocumentEncoder"/>.
/// </remarks>
public sealed class Jbig2ImageEncoder : ImageEncoder
{
    /// <summary>JBIG2 coding options. Default: lossless, standalone file.</summary>
    public Jbig2Options Encoding { get; init; } = new();

    /// <summary>How pixels become black or white. Default: the reference encoder's background-cleaning mode.</summary>
    public BinarizationOptions Binarization { get; init; } = new();

    /// <inheritdoc />
    protected override void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);

        BinaryBitmap bitmap = image.ToJbig2Bitmap(Binarization);
        cancellationToken.ThrowIfCancellationRequested();

        Jbig2Document document = Jbig2Encoder.Encode(bitmap, Encoding);
        byte[] data = document.ToSingleStream();
        stream.Write(data, 0, data.Length);
    }
}
