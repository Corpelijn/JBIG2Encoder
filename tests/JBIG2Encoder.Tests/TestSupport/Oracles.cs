using System;
using JBig2Decoder.NETStandard;

namespace JBIG2Encoder.Tests.TestSupport;

/// <summary>Independent third-party decoders used to validate the encoder's output.</summary>
internal static class Oracles
{
    /// <summary>
    /// Decodes with JBig2Decoder.NETStandard (a port of the JPedal decoder). For PDF-embedded streams pass
    /// the globals stream separately, exactly as a PDF reader would.
    /// </summary>
    public static BinaryBitmap DecodeWithJBig2Decoder(byte[] data, byte[]? globals = null)
    {
        var decoder = new JBIG2StreamDecoder();
        if (globals != null) decoder.SetGlobalData(globals);

        byte[] rgb = decoder.DecodeJBIG2(data, out int width, out int height);

        var bmp = new BinaryBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // The decoder reports white as 0xFF and black as 0x00.
                if (rgb[(y * width + x) * 3] == 0) bmp.SetPixel(x, y, true);
            }
        }
        return bmp;
    }
}
