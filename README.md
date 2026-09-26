# JBIG2Encoder

> **Notice: AI-generated port, not maintained.**
> This library is a conversion of [jbig2enc](https://github.com/agl/jbig2enc) to C# that was **produced with AI assistance
> (Anthropic's Claude)**. The author **does not intend to maintain it** — do not expect bug fixes, security updates, new
> features or support. It is published as-is under the Apache-2.0 license, without warranty of any kind. Evaluate it on your
> own data and in your own target viewers before relying on it. Forks are welcome.

![NuGet Version](https://img.shields.io/nuget/v/JBIG2Encoder?label=JBIG2Encoder&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FJBIG2Encoder)
![NuGet Version](https://img.shields.io/nuget/v/JBIG2Encoder.ImageSharp?label=JBIG2Encoder.ImageSharp&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FJBIG2Encoder.ImageSharp)
![NuGet Version](https://img.shields.io/nuget/v/JBIG2Encoder?label=JBIG2Encoder.SkiaSharp&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FJBIG2Encoder.SkiaSharp)
![NuGet Version](https://img.shields.io/nuget/v/JBIG2Encoder?label=JBIG2Encoder.NetVips&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FJBIG2Encoder.NetVips)

A pure managed **JBIG2 encoder for .NET** — no native libraries, no P/Invoke. It is a C# port of the algorithms in
[agl/jbig2enc](https://github.com/agl/jbig2enc) (Apache-2.0) with the Leptonica dependency replaced by managed code, plus
ready-made integrations for **ImageSharp**, **SkiaSharp** and **NetVips**.

| Package | What it gives you |
|---|---|
| `JBIG2Encoder` | The encoder: `BinaryBitmap`, `Binarizer`, `Jbig2Encoder`, `Jbig2DocumentEncoder`. No dependencies. |
| `JBIG2Encoder.ImageSharp` | Registers a JBIG2 format/encoder: `image.Save("page.jb2")`, `image.SaveAsJbig2(...)`. |
| `JBIG2Encoder.SkiaSharp` | `SKBitmap` / `SKImage` / `SKPixmap` extension methods. |
| `JBIG2Encoder.NetVips` | `Image.Jbig2save`, `Jbig2saveBuffer`, `Jbig2saveStream`. |

Targets **net8.0**. Encoding only — there is no JBIG2 decoder in this repository.

## Two modes — pick deliberately

| | `Jbig2Mode.Lossless` (default) | `Jbig2Mode.Lossy` |
|---|---|---|
| Technique | Generic region (arithmetic-coded bitmap) | Symbol dictionary + text region |
| Result | Decodes to exactly the input bitmap | Each shape is replaced by the first similar shape seen |
| Size | Baseline | Much smaller for text pages (several times smaller on clean print; the saving depends heavily on the content) |
| Risk | None | **Substitution errors**: too low a threshold can turn a 6 into an 8. |

> **Lossy mode can change what a document says.** Digits and look-alike letters are the classic victims, and this is why
> OCRmyPDF removed its lossy mode. If you use it, keep `SymbolThreshold` high (0.95 or more for numbers/forms), review the
> output, and never use it for anything where a wrong digit matters. The default is lossless.

The "lossless" claim is about the **bi-level** image: JBIG2 stores 1-bit pixels, so a gray or color source is first
binarized (below). Everything after that is exact.

Lossless symbol coding through *generic refinement* is **not** implemented, on purpose: the reference encoder documents it
as broken (it also crashes Adobe Acrobat) and no working implementation exists to port from.

## Quick start

### Core

```csharp
using JBIG2Encoder;

// Any gray/color pixel buffer -> black & white -> JBIG2 (standalone .jb2 file bytes).
BinaryBitmap page = Binarizer.FromPixels(rgbaBytes, width, height, stride, PixelLayout.Rgba32);
byte[] file = Jbig2Encoder.EncodeGenericRegion(page);                      // lossless

// Lossy, several pages, as PDF-style streams:
var encoder = new Jbig2DocumentEncoder(new Jbig2Options
{
    Mode = Jbig2Mode.Lossy,
    Output = Jbig2Output.PdfEmbedded,
    SymbolThreshold = 0.95f,
});
encoder.AddPage(page1);
encoder.AddPage(page2);
Jbig2Document doc = encoder.Finish();
// doc.Globals   -> shared symbols (null when there are none)
// doc.Pages[i]  -> the stream for page i
```

### ImageSharp

```csharp
using JBIG2Encoder.ImageSharp;

Configuration.Default.AddJbig2Encoder();      // once, at startup
image.Save("page.jb2");                       // picked by extension (.jb2 / .jbig2)

image.SaveAsJbig2("page.jb2", new Jbig2ImageEncoder
{
    Encoding = new Jbig2Options { Mode = Jbig2Mode.Lossy, SymbolThreshold = 0.95f },
    Binarization = new BinarizationOptions { Mode = BinarizationMode.Global, Threshold = 160 },
});
```

Targets **ImageSharp 3.1.x** (`[3.1.12, 4.0)`). See [Licensing](#licensing) — ImageSharp itself is not Apache-2.0.

### SkiaSharp

SkiaSharp cannot register new encoded formats, so this is a set of extension methods:

```csharp
using JBIG2Encoder.SkiaSharp;

byte[] jb2 = bitmap.EncodeJbig2ToBytes(dpi: 300);
bitmap.SaveJbig2("page.jb2", new Jbig2ImageOptions { Encoding = { Mode = Jbig2Mode.Lossless } });
```

### NetVips

libvips has no JBIG2 saver and NetVips cannot add one, so the extension methods are named after the generated savers:

```csharp
using JBIG2Encoder.NetVips;

image.Jbig2save("page.jb2");
byte[] bytes = image.Jbig2saveBuffer();
```

### Multi-page documents from any library

Every integration offers `ToJbig2Bitmap()`, which yields the `BinaryBitmap` that `Jbig2DocumentEncoder.AddPage` takes.

## Binarization

JBIG2 is 1-bit only. `Binarizer` reproduces what the jbig2enc command line tool does before encoding: estimate the local
paper level per tile, normalize it to white (removing gradients and tint), then apply a fixed threshold (default 200;
`BinarizationMode.Global` uses 128 and skips the cleaning). Optional 2x/4x interpolated upsampling is available. Differences
from jbig2enc: color is reduced with real luma by default (jbig2enc uses only the green channel — `GrayConversion.Green`
reproduces that), and transparent pixels count as white.

## Embedding in a PDF

Use `Jbig2Output.PdfEmbedded`. Each page stream becomes an image XObject with the `/JBIG2Decode` filter, and the globals
(if any) become one stream referenced from every page's image:

```
5 0 obj << /Type /XObject /Subtype /Image /Width W /Height H /ColorSpace /DeviceGray /BitsPerComponent 1
           /Filter /JBIG2Decode /DecodeParms << /JBIG2Globals 9 0 R >> /Length ... >> stream ...page bytes... endstream
9 0 obj << /Length ... >> stream ...doc.Globals bytes... endstream
```

Omit `/DecodeParms` when `doc.Globals` is `null`. No PDF writer is included; `tests/JBIG2Encoder.Tests/TestSupport/PdfBuilder.cs`
shows a minimal one.

## How it relates to jbig2enc

* **Lossless output is byte-for-byte identical** to jbig2enc 0.32's generic-region output for the same bitmap (with and
  without `-d`, standalone and PDF-embedded).
* **Lossy output decodes to the same picture** (apart from the page-edge case below) but is not guaranteed to be
  byte-identical: jbig2enc orders symbols and strips that compare equal with `std::sort`, whose order for ties is
  unspecified, while this library orders them deterministically. In practice the sizes differ by well under 1 %.
* **Page-edge placement.** For shapes within 6 px of the top or left edge of the page, Leptonica clips its alignment window at
  the page edge, which makes jbig2enc place such symbols up to 1 px off. This library aligns them correctly, so for those
  shapes the decoded page differs from jbig2enc's (and matches the source).
* **Refinement / lossless symbol mode: not ported** (see above). Halftone and MMR were never in jbig2enc either.
* **Leptonica is replaced.** Connected components, the correlation classifier, centroid alignment and the background
  normalization are re-implemented in managed code following Leptonica's algorithms.
* **No component size cutoff.** Leptonica silently drops components larger than its size limit (jbig2enc passes 9999 px), which
  would delete large rules or images from a page. Here every component is coded.
* **Default threshold 0.92 / weight 0.5**, the same as jbig2enc's command line defaults.
* **Resolution** is converted to the pixels-per-metre unit the JBIG2 page-information segment requires (jbig2enc writes raw dpi).
* Multi-page standalone files get an **end-of-file segment** (jbig2enc's never triggers).

## Performance

Compared with the native `jbig2` executable (jbig2enc 0.32, the conda-forge MSVC build linked against Leptonica 1.87) on the same
1-bit pages. Both are single-threaded. Medians (21 in-process runs of this library, 7 process runs of jbig2enc), Windows 10,
.NET 8.0.22, 16 logical cores; times in milliseconds.

| Page | Mode | jbig2enc (process) | jbig2enc minus start-up | This library | Speed-up vs. process / vs. minus start-up |
|---|---|---:|---:|---:|---:|
| A4 @ 300 dpi, rendered text (2480×3508) | lossless | 76.5 | 50.8 | 31.7 | 2.4× / 1.6× |
| | lossless, TPGD (`-d`) | 64.7 | 38.9 | 19.3 | 3.3× / 2.0× |
| | lossy (`-s`) | 77.4 | 51.7 | 23.1 | 3.4× / 2.2× |
| A4 @ 300 dpi, text + scanner noise | lossless | 76.3 | 50.5 | 33.0 | 2.3× / 1.5× |
| | lossless, TPGD (`-d`) | 77.9 | 52.1 | 33.1 | 2.4× / 1.6× |
| | lossy (`-s`) | 134.3 | 108.6 | 51.2 | 2.6× / 2.1× |
| A4 @ 600 dpi, rendered text (4960×7016) | lossless | 217.9 | 192.2 | 117.5 | 1.9× / 1.6× |
| | lossless, TPGD (`-d`) | 166.9 | 141.2 | 68.5 | 2.4× / 2.1× |
| | lossy (`-s`) | 158.1 | 132.3 | 51.3 | 3.1× / 2.6× |
| 2000×2000, 30 % random noise (worst case) | lossless | 84.6 | 58.8 | 44.6 | 1.9× / 1.3× |

* **How it was measured.** jbig2enc is run as a process on a `.pbm` file, so its time includes process start-up (about 26 ms,
  measured on an 8×8 image and shown separately in the "minus start-up" column), reading the file and writing the result. This
  library is timed in-process on a bitmap that is already in memory, after warm-up. The "minus start-up" column is the fairer
  comparison for an application that encodes many pages; the first call in a fresh process (JIT included) took 31.5 ms for the
  A4 @ 300 dpi page. Loading and binarizing the source image is not included for either side.
* **Not a document.** Symbol coding of dense random noise (a huge number of tiny, mostly different shapes) took 24.5 s
  here and 43.9 s with jbig2enc for a single run; use lossless mode for content like that.
* Another jbig2enc build (for example GCC on Linux) may be faster than the one measured, and results depend on the machine.

To repeat the comparison (including checking that the outputs match), point the tool at a `jbig2` executable:

```
dotnet run --project tools/ReferenceComparison -c Release -- path/to/jbig2
```

## Licensing

* This project: **Apache-2.0** (see `LICENSE`). It is a derivative of jbig2enc (Apache-2.0, © Google) and re-implements
  algorithms from Leptonica (BSD 2-clause); see `NOTICE`.
* **ImageSharp is not Apache-2.0.** `JBIG2Encoder.ImageSharp` depends on SixLabors.ImageSharp 3.x, which is under the
  *Six Labors Split License*: free for open source, non-profits and companies under US$1M annual revenue, otherwise a
  commercial license is required. 4.x has the identical license text; only ImageSharp 2.x and earlier are Apache-2.0.
  The core, SkiaSharp and NetVips packages do not depend on ImageSharp.
* **Patents.** jbig2enc warns that JBIG2 "may be patented". The upstream project's issue #58 lists the relevant patents as
  expired; this is not legal advice.

## Building and testing

```
dotnet build
dotnet test
```

A small command line sample modelled on jbig2enc's flags is in `samples/Jbig2Cli`.
