using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Teleprompter.Import;

internal static partial class DocxImporter
{
    public static ImportResult Import(byte[] bytes)
    {
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var document = WordprocessingDocument.Open(memory, false);
            var main = document.MainDocumentPart;
            var body = main?.Document?.Body;
            if (main is null || body is null)
            {
                throw new ImportException("El archivo de Word no tiene contenido.");
            }

            var context = new DocxContext(main);
            var builder = new MarkdownBuilder();
            foreach (var element in body.ChildElements)
            {
                Visit(element, context, builder);
            }

            if (builder.IsEmpty)
            {
                throw new ImportException("No se encontró texto en el documento de Word.");
            }

            return new ImportResult(builder.ToString(), null);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or System.Xml.XmlException)
        {
            throw new ImportException("El archivo de Word está dañado o no es un .docx válido.", ex);
        }
    }

    private static void Visit(OpenXmlElement element, DocxContext context, MarkdownBuilder builder)
    {
        switch (element)
        {
            case Paragraph paragraph:
                EmitParagraph(paragraph, context, builder);
                break;
            case Table table:
                foreach (var row in table.Elements<TableRow>())
                {
                    foreach (var cell in row.Elements<TableCell>())
                    {
                        foreach (var child in cell.ChildElements)
                        {
                            Visit(child, context, builder);
                        }
                    }
                }

                break;
            case SdtBlock sdt when sdt.SdtContentBlock is not null:
                foreach (var child in sdt.SdtContentBlock.ChildElements)
                {
                    Visit(child, context, builder);
                }

                break;
            case CustomXmlBlock custom:
                foreach (var child in custom.ChildElements)
                {
                    Visit(child, context, builder);
                }

                break;
        }
    }

    private static void EmitParagraph(Paragraph paragraph, DocxContext context, MarkdownBuilder builder)
    {
        var spans = CollectSpans(paragraph);
        if (spans.All(s => string.IsNullOrWhiteSpace(s.Text)))
        {
            return;
        }

        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;

        var headingLevel = context.GetHeadingLevel(styleId, properties?.OutlineLevel?.Val?.Value);
        if (headingLevel > 0)
        {
            builder.Heading(headingLevel, string.Concat(spans.Select(s => s.Text)));
            return;
        }

        if (context.TryGetListItem(properties?.NumberingProperties, styleId, out var ordered, out var number, out var depth))
        {
            builder.ListItem(spans, ordered, number, depth);
            return;
        }

        builder.Paragraph(spans);
    }

    private static List<TextSpan> CollectSpans(Paragraph paragraph)
    {
        var spans = new List<TextSpan>();
        foreach (var run in paragraph.Descendants<Run>())
        {
            // Los cuadros de texto anidados contienen sus propios parrafos; se omiten para no duplicar texto.
            if (!ReferenceEquals(run.Ancestors<Paragraph>().FirstOrDefault(), paragraph))
            {
                continue;
            }

            var properties = run.RunProperties;
            if (IsOn(properties?.Vanish))
            {
                continue;
            }

            var text = new StringBuilder();
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text t:
                        text.Append(t.Text);
                        break;
                    case TabChar:
                        text.Append(' ');
                        break;
                    case Break br:
                        var type = br.Type?.InnerText;
                        text.Append(type is "page" or "column" ? ' ' : '\n');
                        break;
                    case CarriageReturn:
                        text.Append('\n');
                        break;
                    case NoBreakHyphen:
                        text.Append('-');
                        break;
                }
            }

            if (text.Length > 0)
            {
                spans.Add(new TextSpan(text.ToString(), IsOn(properties?.Bold), IsOn(properties?.Italic)));
            }
        }

        return spans;
    }

    private static bool IsOn(OnOffType? value) => value is not null && (value.Val is null || value.Val.Value);

    private sealed partial class DocxContext
    {
        private readonly Dictionary<string, Style> _styles = [];
        private readonly Dictionary<int, int> _abstractByNumbering = [];
        private readonly Dictionary<int, AbstractNum> _abstracts = [];
        private readonly Dictionary<(int NumberingId, int Level), int> _counters = [];

        public DocxContext(MainDocumentPart main)
        {
            var styles = main.StyleDefinitionsPart?.Styles;
            if (styles is not null)
            {
                foreach (var style in styles.Elements<Style>())
                {
                    if (style.StyleId?.Value is { } id)
                    {
                        _styles[id] = style;
                    }
                }
            }

            var numbering = main.NumberingDefinitionsPart?.Numbering;
            if (numbering is not null)
            {
                foreach (var abstractNum in numbering.Elements<AbstractNum>())
                {
                    if (abstractNum.AbstractNumberId?.Value is int abstractId)
                    {
                        _abstracts[abstractId] = abstractNum;
                    }
                }

                foreach (var instance in numbering.Elements<NumberingInstance>())
                {
                    if (instance.NumberID?.Value is int numberingId && instance.AbstractNumId?.Val?.Value is int abstractId)
                    {
                        _abstractByNumbering[numberingId] = abstractId;
                    }
                }
            }
        }

        public int GetHeadingLevel(string? styleId, int? outlineLevel)
        {
            if (outlineLevel is >= 0 and < 6)
            {
                return outlineLevel.Value + 1;
            }

            // Recorre la herencia de estilos: un estilo propio basado en "heading 2" tambien es titulo.
            var current = styleId;
            for (var depth = 0; depth < 8 && current is not null && _styles.TryGetValue(current, out var style); depth++)
            {
                var name = style.StyleName?.Val?.Value?.Trim().ToLowerInvariant() ?? string.Empty;
                if (name is "title" or "título" or "titulo")
                {
                    return 1;
                }

                if (name is "subtitle" or "subtítulo" or "subtitulo")
                {
                    return 2;
                }

                var match = HeadingName().Match(name);
                if (match.Success)
                {
                    return Math.Min(6, int.Parse(match.Groups[1].Value));
                }

                var styleOutline = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
                if (styleOutline is >= 0 and < 6)
                {
                    return styleOutline.Value + 1;
                }

                current = style.BasedOn?.Val?.Value;
            }

            return 0;
        }

        public bool TryGetListItem(NumberingProperties? numbering, string? styleId, out bool ordered, out int number, out int depth)
        {
            ordered = false;
            number = 0;
            depth = 0;

            var numberingId = numbering?.NumberingId?.Val?.Value;
            var level = numbering?.NumberingLevelReference?.Val?.Value;

            if (numberingId is null && styleId is not null && _styles.TryGetValue(styleId, out var style))
            {
                var styleNumbering = style.StyleParagraphProperties?.NumberingProperties;
                numberingId = styleNumbering?.NumberingId?.Val?.Value;
                level ??= styleNumbering?.NumberingLevelReference?.Val?.Value;
            }

            if (numberingId is not int id || id == 0)
            {
                return false;
            }

            var itemDepth = Math.Clamp(level ?? 0, 0, 8);
            depth = itemDepth;
            var format = GetLevelFormat(id, itemDepth, out var start);
            ordered = format is not null && format != "bullet" && format != "none";

            foreach (var key in _counters.Keys.Where(k => k.NumberingId == id && k.Level > itemDepth).ToList())
            {
                _counters.Remove(key);
            }

            var counterKey = (id, itemDepth);
            number = _counters.TryGetValue(counterKey, out var current) ? current + 1 : start;
            _counters[counterKey] = number;
            return true;
        }

        private string? GetLevelFormat(int numberingId, int level, out int start)
        {
            start = 1;
            if (!_abstractByNumbering.TryGetValue(numberingId, out var abstractId) || !_abstracts.TryGetValue(abstractId, out var abstractNum))
            {
                return null;
            }

            var definition = abstractNum.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
            if (definition is null)
            {
                return null;
            }

            start = definition.StartNumberingValue?.Val?.Value ?? 1;
            return definition.NumberingFormat?.Val?.InnerText;
        }

        [GeneratedRegex(@"^(?:heading|t[ií]tulo|encabezado)\s*([1-9])$")]
        private static partial Regex HeadingName();
    }
}
