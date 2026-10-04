using System.Text;
using System.Text.RegularExpressions;

namespace Teleprompter.Import;

public readonly record struct TextSpan(string Text, bool Bold = false, bool Italic = false);

/// <summary>
/// Construye el Markdown interno del guion. Todo formato importado termina como Markdown porque
/// es el mismo texto que el usuario ve y corrige en el modo de edicion.
/// </summary>
public sealed partial class MarkdownBuilder
{
    private readonly StringBuilder _output = new();
    private int _lastListDepth = -1;

    public void Heading(int level, string text)
    {
        var clean = CollapseSpaces(CleanText(text).Replace('\n', ' ')).Trim();
        if (clean.Length == 0)
        {
            return;
        }

        AppendBlock(new string('#', Math.Clamp(level, 1, 6)) + " " + EscapeInline(clean));
        _lastListDepth = -1;
    }

    public void Paragraph(string text) => Paragraph([new TextSpan(text)]);

    public void Paragraph(IReadOnlyList<TextSpan> spans)
    {
        var markdown = FormatSpans(spans);
        if (markdown.Length == 0)
        {
            return;
        }

        AppendBlock(markdown);
        _lastListDepth = -1;
    }

    public void ListItem(IReadOnlyList<TextSpan> spans, bool ordered, int number, int depth)
    {
        var markdown = FormatSpans(spans);
        if (markdown.Length == 0)
        {
            return;
        }

        // Un elemento mas profundo que su predecesor inmediato mas uno se interpretaria como bloque de codigo.
        depth = Math.Clamp(depth, 0, _lastListDepth + 1);
        var marker = ordered ? $"{Math.Max(1, number)}." : "-";
        var indent = new string(' ', depth * 4);
        var continuation = "\n" + indent + new string(' ', marker.Length + 1);
        AppendBlock(indent + marker + " " + markdown.Replace("\n", continuation));
        _lastListDepth = depth;
    }

    public void Separator()
    {
        AppendBlock("---");
        _lastListDepth = -1;
    }

    public override string ToString() => _output.ToString().TrimEnd() + "\n";

    public bool IsEmpty => _output.Length == 0;

    /// <summary>Convierte texto plano en Markdown que se muestra literalmente, conservando los saltos de linea.</summary>
    public static string EscapeText(string text)
    {
        var lines = CleanText(text).Split('\n');
        var builder = new StringBuilder();
        var blank = 0;
        foreach (var raw in lines)
        {
            var line = CollapseSpaces(raw).Trim();
            if (line.Length == 0)
            {
                blank++;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(blank > 0 ? "\n\n" : "\n");
            }

            builder.Append(EscapeLineStart(EscapeInline(line)));
            blank = 0;
        }

        return builder.ToString();
    }

    private static string FormatSpans(IReadOnlyList<TextSpan> spans)
    {
        var merged = new List<TextSpan>();
        foreach (var span in spans)
        {
            var text = CleanText(span.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (merged.Count > 0 && merged[^1].Bold == span.Bold && merged[^1].Italic == span.Italic)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + text };
            }
            else
            {
                merged.Add(span with { Text = text });
            }
        }

        var builder = new StringBuilder();
        foreach (var span in merged)
        {
            if ((!span.Bold && !span.Italic) || string.IsNullOrWhiteSpace(span.Text))
            {
                builder.Append(EscapeInline(span.Text));
                continue;
            }

            // Los delimitadores de enfasis deben quedar pegados al texto, nunca a un espacio o salto.
            var marker = span.Bold && span.Italic ? "***" : span.Bold ? "**" : "*";
            var lines = span.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                var line = lines[i];
                var start = 0;
                while (start < line.Length && char.IsWhiteSpace(line[start]))
                {
                    start++;
                }

                var end = line.Length;
                while (end > start && char.IsWhiteSpace(line[end - 1]))
                {
                    end--;
                }

                builder.Append(line, 0, start);
                if (end > start)
                {
                    builder.Append(marker).Append(EscapeInline(line[start..end])).Append(marker);
                }

                builder.Append(line, end, line.Length - end);
            }
        }

        var result = new StringBuilder();
        foreach (var raw in builder.ToString().Split('\n'))
        {
            var line = CollapseSpaces(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(EscapeLineStart(line));
        }

        return result.ToString();
    }

    private void AppendBlock(string block)
    {
        if (_output.Length > 0)
        {
            _output.Append("\n\n");
        }

        _output.Append(block);
    }

    /// <summary>Normaliza saltos de linea y elimina caracteres de control e invisibles.</summary>
    internal static string CleanText(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                    builder.Append('\n');
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n':
                    builder.Append('\n');
                    break;
                case '\t':
                case '\u00A0':
                case '\u2007':
                case '\u202F':
                    builder.Append(' ');
                    break;
                case '\u00AD':
                case '\u200B':
                case '\uFEFF':
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    internal static string CollapseSpaces(string text) => MultipleSpaces().Replace(text, " ");

    private static string EscapeInline(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                case '`':
                case '*':
                case '_':
                case '[':
                case ']':
                case '<':
                    builder.Append('\\').Append(c);
                    break;
                case '&' when i + 1 < text.Length && (char.IsAsciiLetter(text[i + 1]) || text[i + 1] == '#'):
                    builder.Append("\\&");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Evita que el inicio de una linea se interprete como titulo, cita, lista o separador.</summary>
    private static string EscapeLineStart(string line)
    {
        if (line.Length == 0)
        {
            return line;
        }

        if (line[0] == '>')
        {
            return "\\" + line;
        }

        if (line[0] == '#')
        {
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
            {
                hashes++;
            }

            return hashes == line.Length || line[hashes] == ' ' ? "\\" + line : line;
        }

        if (line[0] is '-' or '+' or '=')
        {
            var onlyMarks = line.All(ch => ch == line[0] || ch == ' ');
            var followedBySpace = line.Length == 1 || line[1] == ' ';
            return onlyMarks || (line[0] != '=' && followedBySpace) ? "\\" + line : line;
        }

        var match = OrderedMarker().Match(line);
        if (match.Success)
        {
            var digits = match.Groups[1].Length;
            return line[..digits] + "\\" + line[digits..];
        }

        return line;
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"^(\d{1,9})[.)](\s|$)")]
    private static partial Regex OrderedMarker();
}
