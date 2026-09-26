using SixLabors.ImageSharp.Formats;

namespace JBIG2Encoder.ImageSharp;

/// <summary>
/// The JBIG2 image format as far as ImageSharp is concerned: it can be encoded (not decoded), and is recognized by the
/// file extensions <c>.jb2</c> and <c>.jbig2</c>.
/// </summary>
public sealed class Jbig2Format : IImageFormat
{
    private Jbig2Format()
    {
    }

    /// <summary>The single instance.</summary>
    public static Jbig2Format Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "JBIG2";

    /// <inheritdoc />
    public string DefaultMimeType => "image/x-jbig2";

    /// <inheritdoc />
    public IEnumerable<string> MimeTypes { get; } = new[] { "image/x-jbig2", "image/jbig2" };

    /// <inheritdoc />
    public IEnumerable<string> FileExtensions { get; } = new[] { "jb2", "jbig2" };
}
