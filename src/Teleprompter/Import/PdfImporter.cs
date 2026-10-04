using System.Text;
using System.Text.RegularExpressions;
using Teleprompter.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Teleprompter.Import;

internal static partial class PdfImporter
{
    /// <summary>Por debajo de esta cantidad de caracteres se asume que la pagina es una imagen escaneada.</summary>
    private const int MinCharsForTextPage = 25;

    /// <summary>Fraccion de la altura de pagina que se considera zona de encabezado o pie.</summary>
    private const double MarginBand = 0.1;

    public static async Task<ImportResult> ImportAsync(byte[] bytes, IProgress<string>? progress, CancellationToken cancellation)
    {
        var pages = new List<List<PdfParagraph>>();
        var ocrPages = 0;
        var ocrFailures = 0;
        var ocrUnavailable = false;
        PdfOcr? ocr = null;

        try
        {
            using var document = PdfDocument.Open(bytes);
            var count = document.NumberOfPages;
            for (var number = 1; number <= count; number++)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report($"Leyendo página {number} de {count}");

                var page = document.GetPage(number);
                var paragraphs = ExtractText(page);

                if (CountChars(paragraphs) < MinCharsForTextPage && page.NumberOfImages > 0)
                {
                    ocr ??= await PdfOcr.CreateAsync(bytes);
                    if (!ocr.IsAvailable)
                    {
                        ocrUnavailable = true;
                    }
                    else
                    {
                        progress?.Report($"Reconociendo texto escaneado: página {number} de {count}");
                        try
                        {
                            var recognized = await ocr.RecognizeAsync(number - 1, cancellation);
                            if (CountChars(recognized) > CountChars(paragraphs))
                            {
                                paragraphs = recognized;
                                ocrPages++;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            ocrFailures++;
                            AppStorage.LogError($"OCR de la pagina {number}", ex);
                        }
                    }
                }

                pages.Add(paragraphs);
            }
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new ImportException("El PDF está protegido con contraseña. Quita la protección e inténtalo de nuevo.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImportException)
        {
            throw new ImportException("No se pudo leer el PDF. Puede estar dañado o usar un formato no compatible.", ex);
        }
        finally
        {
            ocr?.Dispose();
        }

        RemoveRepeatedMargins(pages);
        var paragraphsInOrder = pages.SelectMany(p => p).Where(p => !IsPageNumber(p.Text)).ToList();
        MarkHeadings(paragraphsInOrder);
        var merged = MergeContinuations(paragraphsInOrder);

        var builder = new MarkdownBuilder();
        foreach (var paragraph in merged)
        {
            if (paragraph.HeadingLevel > 0)
            {
                builder.Heading(paragraph.HeadingLevel, paragraph.Text);
            }
            else if (paragraph.IsListItem)
            {
                builder.ListItem([new TextSpan(paragraph.Text)], ordered: false, number: 0, depth: 0);
            }
            else
            {
                builder.Paragraph(paragraph.Text);
            }
        }

        if (builder.IsEmpty)
        {
            var reason = ocrUnavailable
                ? "El PDF parece escaneado y Windows no tiene instalado un idioma con reconocimiento de texto (OCR). Agrégalo en Configuración > Hora e idioma > Idioma."
                : "No se encontró texto en el PDF.";
            throw new ImportException(reason);
        }

        return new ImportResult(builder.ToString(), BuildNotice(ocrPages, ocrFailures, ocrUnavailable));
    }

    private static string? BuildNotice(int ocrPages, int ocrFailures, bool ocrUnavailable)
    {
        var parts = new List<string>();
        if (ocrPages > 0)
        {
            parts.Add(ocrPages == 1
                ? "Se reconoció el texto de 1 página escaneada; revisa que no haya errores."
                : $"Se reconoció el texto de {ocrPages} páginas escaneadas; revisa que no haya errores.");
        }

        if (ocrFailures > 0)
        {
            parts.Add($"No se pudo reconocer el texto de {ocrFailures} página(s).");
        }

        if (ocrUnavailable)
        {
            parts.Add("Algunas páginas escaneadas se omitieron porque Windows no tiene un idioma con OCR instalado.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static List<PdfParagraph> ExtractText(Page page)
    {
        var letters = page.Letters;
        if (letters.Count == 0)
        {
            return [];
        }

        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks).OrderBy(b => b.ReadingOrder);

        var assembler = new ParagraphAssembler();
        var height = page.Height > 0 ? page.Height : 1;
        foreach (var block in ordered)
        {
            // El origen de coordenadas del PDF esta abajo a la izquierda.
            var box = block.BoundingBox;
            assembler.SetPageMargin(box.Bottom >= height * (1 - MarginBand) || box.Top <= height * MarginBand);

            foreach (var line in block.TextLines)
            {
                double sizeSum = 0;
                var letterCount = 0;
                foreach (var word in line.Words)
                {
                    foreach (var letter in word.Letters)
                    {
                        if (!string.IsNullOrWhiteSpace(letter.Value))
                        {
                            sizeSum += letter.PointSize;
                            letterCount++;
                        }
                    }
                }

                assembler.AddLine(line.Text, letterCount > 0 ? sizeSum / letterCount : 0, letterCount);
            }

            assembler.Break();
        }

        return assembler.Finish();
    }

    private static int CountChars(IEnumerable<PdfParagraph> paragraphs)
    {
        return paragraphs.Sum(p => p.Text.Count(c => !char.IsWhiteSpace(c)));
    }

    /// <summary>Quita encabezados y pies de pagina que se repiten en la mayoria de las paginas.</summary>
    private static void RemoveRepeatedMargins(List<List<PdfParagraph>> pages)
    {
        var pagesWithText = pages.Count(p => p.Count > 0);
        if (pagesWithText < 3)
        {
            return;
        }

        var counts = new Dictionary<string, int>();
        foreach (var page in pages)
        {
            foreach (var key in MarginCandidates(page).Select(p => MarginKey(p.Text)).Distinct())
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var threshold = Math.Max(3, (int)Math.Ceiling(pagesWithText * 0.5));
        var repeated = counts.Where(kv => kv.Value >= threshold).Select(kv => kv.Key).ToHashSet();
        if (repeated.Count == 0)
        {
            return;
        }

        foreach (var page in pages)
        {
            foreach (var candidate in MarginCandidates(page).ToList())
            {
                if (candidate.Text.Length <= 120 && repeated.Contains(MarginKey(candidate.Text)))
                {
                    page.Remove(candidate);
                }
            }
        }
    }

    private static IEnumerable<PdfParagraph> MarginCandidates(List<PdfParagraph> page)
    {
        return page.Where(p => p.InPageMargin);
    }

    private static string MarginKey(string text) => Digits().Replace(text.ToLowerInvariant(), "#").Trim();

    private static bool IsPageNumber(string text) => PageNumber().IsMatch(text.Trim());

    /// <summary>Detecta titulos por tamano de letra relativo al cuerpo del documento.</summary>
    private static void MarkHeadings(List<PdfParagraph> paragraphs)
    {
        var sized = paragraphs.Where(p => p.FontSize > 0 && !p.IsListItem).OrderBy(p => p.FontSize).ToList();
        if (sized.Count < 3)
        {
            return;
        }

        var total = sized.Sum(p => p.Text.Length);
        double body = sized[^1].FontSize;
        var accumulated = 0;
        foreach (var paragraph in sized)
        {
            accumulated += paragraph.Text.Length;
            if (accumulated * 2 >= total)
            {
                body = paragraph.FontSize;
                break;
            }
        }

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.IsListItem || paragraph.FontSize < body * 1.2 || paragraph.Text.Length > 150 || EndsWithSentence(paragraph.Text))
            {
                continue;
            }

            paragraph.HeadingLevel = paragraph.FontSize >= body * 1.6 ? 1 : 2;
        }
    }

    /// <summary>Une parrafos cortados por un salto de pagina o de columna.</summary>
    private static List<PdfParagraph> MergeContinuations(List<PdfParagraph> paragraphs)
    {
        var result = new List<PdfParagraph>();
        foreach (var paragraph in paragraphs)
        {
            if (result.Count > 0)
            {
                var previous = result[^1];
                var continues = previous.HeadingLevel == 0
                    && paragraph.HeadingLevel == 0
                    && !paragraph.IsListItem
                    && char.IsLower(paragraph.Text[0])
                    && !EndsWithTerminal(previous.Text);

                if (continues)
                {
                    var joined = new StringBuilder(previous.Text);
                    ParagraphAssembler.JoinLine(joined, paragraph.Text);
                    previous.Text = joined.ToString();
                    continue;
                }
            }

            result.Add(paragraph);
        }

        return result;
    }

    private static bool EndsWithSentence(string text) => text.Length > 0 && ".!?".Contains(text[^1]);

    private static bool EndsWithTerminal(string text) => text.Length > 0 && ".!?:;…\"”»)]".Contains(text[^1]);

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^((p[aá]g(ina)?\.?|page)\s*)?\d{1,4}(\s*(de|of|/)\s*\d{1,4})?$|^[\-\u2013\u2014]\s*\d{1,4}\s*[\-\u2013\u2014]$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();
}
