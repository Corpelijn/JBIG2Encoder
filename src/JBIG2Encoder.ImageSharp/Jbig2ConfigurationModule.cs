using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace JBIG2Encoder.ImageSharp;

/// <summary>Registers the JBIG2 format and encoder with an ImageSharp <see cref="Configuration"/>.</summary>
public sealed class Jbig2ConfigurationModule : IImageFormatConfigurationModule
{
    private readonly Jbig2ImageEncoder _encoder;

    /// <summary>Creates a module that registers the default (lossless) encoder.</summary>
    public Jbig2ConfigurationModule()
        : this(null)
    {
    }

    /// <summary>Creates a module that registers the given encoder.</summary>
    /// <param name="encoder">The encoder to use for <c>.jb2</c> / <c>.jbig2</c> files; <c>null</c> for the defaults.</param>
    public Jbig2ConfigurationModule(Jbig2ImageEncoder? encoder)
    {
        _encoder = encoder ?? new Jbig2ImageEncoder();
    }

    /// <inheritdoc />
    public void Configure(Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.ImageFormatsManager.AddImageFormat(Jbig2Format.Instance);
        configuration.ImageFormatsManager.SetEncoder(Jbig2Format.Instance, _encoder);
    }
}
