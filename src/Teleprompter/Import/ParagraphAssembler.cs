using System.Text;

namespace Teleprompter.Import;

internal sealed class PdfParagraph(string text, double fontSize, bool isListItem, bool inPageMargin)
{
    public string Text { get; set; } = text;

    public double FontSize { get; } = fontSize;

    public bool IsListItem { get; } = isListItem;

    /// <summary>Esta en la franja superior o inferior de la pagina, donde van encabezados y pies.</summary>
    public bool InPageMargin { get; } = inPageMargin;

    public int HeadingLevel { get; set; }
}

/// <summary>
/// Une las lineas fisicas de una pagina en parrafos reales. En un PDF cada renglon es independiente,
/// pero en el teleprompter el texto debe volver a fluir segun el tamano de letra elegido.
/// </summary>
internal sealed class ParagraphAssembler
{
    // Vinetas tipograficas habituales en PDF: punto, circulo, cuadro, flecha y marca de verificacion.
    private const string Bullets = "\u2022\u25CF\u25AA\u25E6\u2023\u2219\u25A0\u25A1\u27A2\u27A4\u25BA\u2713\u2714";

    private readonly List<PdfParagraph> _result = [];
    private readonly StringBuilder _current = new();
    private double _sizeSum;
    private int _sizeWeight;
    private bool _isListItem;
    private bool _inPageMargin;

    /// <summary>Indica si los renglones siguientes pertenecen a la franja de encabezado o pie de pagina.</summary>
    public void SetPageMargin(bool inPageMargin)
    {
        Break();
        _inPageMargin = inPageMargin;
    }

    public void AddLine(string text, double fontSize, int weight)
    {
        var line = MarkdownBuilder.CollapseSpaces(MarkdownBuilder.CleanText(text).Replace('\n', ' ')).Trim();
        if (line.Length == 0)
        {
            return;
        }

        if (Bullets.Contains(line[0]))
        {
            Break();
            _isListItem = true;
            line = line[1..].TrimStart();
            if (line.Length == 0)
            {
                return;
            }
        }

        JoinLine(_current, line);
        if (fontSize > 0 && weight > 0)
        {
            _sizeSum += fontSize * weight;
            _sizeWeight += weight;
        }
    }

    public void Break()
    {
        if (_current.Length > 0)
        {
            var size = _sizeWeight > 0 ? _sizeSum / _sizeWeight : 0;
            _result.Add(new PdfParagraph(_current.ToString(), size, _isListItem, _inPageMargin));
        }

        _current.Clear();
        _sizeSum = 0;
        _sizeWeight = 0;
        _isListItem = false;
    }

    public List<PdfParagraph> Finish()
    {
        Break();
        return _result;
    }

    /// <summary>Agrega un renglon reparando la division silabica al final de linea ("pala-" + "bra").</summary>
    public static void JoinLine(StringBuilder builder, string line)
    {
        if (builder.Length == 0)
        {
            builder.Append(line);
            return;
        }

        var hyphenated = builder.Length >= 2
            && builder[^1] == '-'
            && char.IsLetter(builder[^2])
            && line.Length > 0
            && char.IsLower(line[0]);

        if (hyphenated)
        {
            builder.Length--;
            builder.Append(line);
        }
        else
        {
            builder.Append(' ').Append(line);
        }
    }
}
