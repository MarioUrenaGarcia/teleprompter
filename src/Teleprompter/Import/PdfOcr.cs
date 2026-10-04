using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace Teleprompter.Import;

/// <summary>
/// Reconocimiento de texto para PDF escaneados usando el motor OCR que incluye Windows,
/// sin dependencias externas. Usa los idiomas instalados en el perfil del usuario.
/// </summary>
internal sealed class PdfOcr : IDisposable
{
    /// <summary>300 ppp: resolucion de referencia para que el OCR distinga acentos y signos.</summary>
    private const double TargetScale = 300.0 / 96.0;

    private readonly WinPdfDocument? _document;
    private readonly OcrEngine? _engine;
    private readonly InMemoryRandomAccessStream? _source;

    private PdfOcr(WinPdfDocument? document, OcrEngine? engine, InMemoryRandomAccessStream? source)
    {
        _document = document;
        _engine = engine;
        _source = source;
    }

    public bool IsAvailable => _document is not null && _engine is not null;

    public static async Task<PdfOcr> CreateAsync(byte[] bytes)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            var fallback = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            if (fallback is not null)
            {
                engine = OcrEngine.TryCreateFromLanguage(fallback);
            }
        }

        if (engine is null)
        {
            return new PdfOcr(null, null, null);
        }

        var source = new InMemoryRandomAccessStream();
        await source.WriteAsync(bytes.AsBuffer());
        source.Seek(0);
        var document = await WinPdfDocument.LoadFromStreamAsync(source);
        return new PdfOcr(document, engine, source);
    }

    public async Task<List<PdfParagraph>> RecognizeAsync(int pageIndex, CancellationToken cancellation)
    {
        if (_document is null || _engine is null)
        {
            return [];
        }

        using var page = _document.GetPage((uint)pageIndex);
        var size = page.Size;
        var longest = Math.Max(size.Width, size.Height);
        if (longest <= 0)
        {
            return [];
        }

        var scale = Math.Min(TargetScale, (OcrEngine.MaxImageDimension - 1) / longest);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(size.Height * scale)),
        };

        using var image = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(image, options);
        cancellation.ThrowIfCancellationRequested();

        var decoder = await BitmapDecoder.CreateAsync(image);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);
        cancellation.ThrowIfCancellationRequested();

        return GroupIntoParagraphs(result);
    }

    public void Dispose()
    {
        _source?.Dispose();
    }

    /// <summary>El OCR entrega renglones sueltos; un espacio vertical mayor al habitual marca un parrafo nuevo.</summary>
    private static List<PdfParagraph> GroupIntoParagraphs(OcrResult result)
    {
        var lines = new List<(string Text, double Top, double Bottom)>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            var top = line.Words.Min(w => w.BoundingRect.Y);
            var bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            lines.Add((line.Text, top, bottom));
        }

        if (lines.Count == 0)
        {
            return [];
        }

        var heights = lines.Select(l => l.Bottom - l.Top).OrderBy(h => h).ToList();
        var typicalHeight = heights[heights.Count / 2];

        var assembler = new ParagraphAssembler();
        double? previousBottom = null;
        foreach (var line in lines)
        {
            if (previousBottom is double bottom && line.Top - bottom > typicalHeight * 0.9)
            {
                assembler.Break();
            }

            assembler.AddLine(line.Text, 0, 0);
            previousBottom = line.Bottom;
        }

        return assembler.Finish();
    }
}
