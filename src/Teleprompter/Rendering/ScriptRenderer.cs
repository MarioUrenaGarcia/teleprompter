using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Teleprompter.Rendering;

public sealed record ScriptStyle(
    FontFamily FontFamily,
    double FontSize,
    FontWeight FontWeight,
    double LineSpacing,
    TextAlignment Alignment,
    Brush Foreground,
    Brush Accent);

/// <summary>
/// Convierte el guion en Markdown a una pila de bloques de texto WPF. Se usa un TextBlock por bloque
/// en lugar de un FlowDocument porque permite desplazar el contenido con precision de subpixel.
/// </summary>
public static class ScriptRenderer
{
    // HTML deshabilitado: en un guion, "<algo>" debe leerse tal cual.
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    public static MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, Pipeline);

    public static string ToPlainText(string markdown)
    {
        var document = Parse(markdown);
        var builder = new StringBuilder();
        foreach (var block in document)
        {
            AppendPlain(block, builder, string.Empty);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static bool HasContent(MarkdownDocument document)
    {
        return document.Any(b => b is not YamlFrontMatterBlock and not LinkReferenceDefinitionGroup and not BlankLineBlock);
    }

    public static void Render(MarkdownDocument document, Panel target, ScriptStyle options)
    {
        target.Children.Clear();
        var context = new RenderContext(options);
        foreach (var block in document)
        {
            RenderBlock(block, target.Children, context, quote: false, inList: false);
        }
    }

    private static void RenderBlock(MdBlock block, UIElementCollection output, RenderContext context, bool quote, bool inList)
    {
        var options = context.Options;
        switch (block)
        {
            case YamlFrontMatterBlock:
            case LinkReferenceDefinitionGroup:
            case BlankLineBlock:
                break;

            case HeadingBlock heading:
            {
                var scale = heading.Level switch { 1 => 1.45, 2 => 1.25, 3 => 1.12, _ => 1.05 };
                var size = options.FontSize * scale;
                var top = output.Count == 0 ? 0 : options.FontSize * 0.35;
                var text = context.CreateTextBlock(size, new Thickness(0, top, 0, options.FontSize * 0.4));
                text.FontWeight = context.Bold;
                AddInlines(text.Inlines, heading.Inline, context, bold: false, italic: quote);
                output.Add(text);
                break;
            }

            case ParagraphBlock paragraph:
            {
                var spacing = inList ? options.FontSize * 0.25 : options.FontSize * 0.55;
                var text = context.CreateTextBlock(options.FontSize, new Thickness(0, 0, 0, spacing));
                AddInlines(text.Inlines, paragraph.Inline, context, bold: false, italic: quote);
                output.Add(text);
                break;
            }

            case ListBlock list:
                RenderList(list, output, context, quote);
                break;

            case QuoteBlock quoteBlock:
            {
                var inner = new StackPanel();
                foreach (var child in quoteBlock)
                {
                    RenderBlock(child, inner.Children, context, quote: true, inList: inList);
                }

                output.Add(new Border
                {
                    BorderBrush = options.Accent,
                    BorderThickness = new Thickness(Math.Max(3, options.FontSize * 0.08), 0, 0, 0),
                    Padding = new Thickness(options.FontSize * 0.5, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, options.FontSize * 0.3),
                    Child = inner,
                });
                break;
            }

            case CodeBlock code:
            {
                var text = context.CreateTextBlock(options.FontSize, new Thickness(0, 0, 0, options.FontSize * 0.55));
                var lines = code.Lines.ToString().Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        text.Inlines.Add(new LineBreak());
                    }

                    text.Inlines.Add(new Run(lines[i]));
                }

                output.Add(text);
                break;
            }

            case ThematicBreakBlock:
                output.Add(new Border
                {
                    Height = Math.Max(2, options.FontSize * 0.05),
                    Background = options.Foreground,
                    Opacity = 0.35,
                    Margin = new Thickness(options.FontSize, options.FontSize * 0.4, options.FontSize, options.FontSize * 0.9),
                });
                break;

            case MdTable table:
                RenderTable(table, output, context);
                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    RenderBlock(child, output, context, quote, inList);
                }

                break;

            case LeafBlock leaf when leaf.Inline is not null:
            {
                var text = context.CreateTextBlock(options.FontSize, new Thickness(0, 0, 0, options.FontSize * 0.55));
                AddInlines(text.Inlines, leaf.Inline, context, bold: false, italic: quote);
                output.Add(text);
                break;
            }
        }
    }

    private static void RenderList(ListBlock list, UIElementCollection output, RenderContext context, bool quote)
    {
        var options = context.Options;
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;

        // Ambito compartido para que todos los marcadores ocupen el mismo ancho y el texto quede alineado.
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, options.FontSize * 0.3) };
        Grid.SetIsSharedSizeScope(container, true);

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = context.CreateTextBlock(options.FontSize, new Thickness(0, 0, options.FontSize * 0.4, 0));
            marker.Text = list.IsOrdered ? $"{number++}." : "•";
            marker.TextAlignment = TextAlignment.Right;
            marker.TextWrapping = TextWrapping.NoWrap;

            var content = new StackPanel();
            foreach (var child in item)
            {
                RenderBlock(child, content.Children, context, quote, inList: true);
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Marker" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(content, 1);
            grid.Children.Add(marker);
            grid.Children.Add(content);
            container.Children.Add(grid);
        }

        output.Add(container);
    }

    private static void RenderTable(MdTable table, UIElementCollection output, RenderContext context)
    {
        var options = context.Options;
        foreach (var row in table.OfType<MdTableRow>())
        {
            var text = context.CreateTextBlock(options.FontSize, new Thickness(0, 0, 0, options.FontSize * 0.3));
            var first = true;
            foreach (var cell in row.OfType<MdTableCell>())
            {
                if (!first)
                {
                    text.Inlines.Add(new Run("   ·   ") { Foreground = options.Accent });
                }

                first = false;
                foreach (var paragraph in cell.OfType<ParagraphBlock>())
                {
                    AddInlines(text.Inlines, paragraph.Inline, context, bold: row.IsHeader, italic: false);
                }
            }

            output.Add(text);
        }
    }

    private static void AddInlines(InlineCollection target, ContainerInline? container, RenderContext context, bool bold, bool italic)
    {
        if (container is null)
        {
            return;
        }

        for (MdInline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(context.CreateRun(literal.Content.ToString(), bold, italic));
                    break;
                case EmphasisInline emphasis:
                    AddInlines(
                        target,
                        emphasis,
                        context,
                        bold || emphasis.DelimiterCount >= 2,
                        italic || emphasis.DelimiterCount % 2 == 1);
                    break;
                case CodeInline code:
                    target.Add(context.CreateRun(code.Content, bold, italic));
                    break;
                case LineBreakInline:
                    // Un salto simple tambien es salto en pantalla: lo que se escribe es lo que se lee.
                    target.Add(new LineBreak());
                    break;
                case LinkInline link when link.IsImage:
                    break;
                case AutolinkInline autolink:
                    target.Add(context.CreateRun(autolink.Url, bold, italic));
                    break;
                case HtmlEntityInline entity:
                    target.Add(context.CreateRun(entity.Transcoded.ToString(), bold, italic));
                    break;
                case HtmlInline html:
                    target.Add(context.CreateRun(html.Tag, bold, italic));
                    break;
                case ContainerInline child:
                    AddInlines(target, child, context, bold, italic);
                    break;
            }
        }
    }

    private static void AppendPlain(MdBlock block, StringBuilder builder, string prefix)
    {
        switch (block)
        {
            case YamlFrontMatterBlock:
            case LinkReferenceDefinitionGroup:
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                builder.Append(prefix).Append(InlineText(leaf.Inline)).AppendLine().AppendLine();
                break;
            case CodeBlock code:
                builder.AppendLine(code.Lines.ToString()).AppendLine();
                break;
            case ListBlock list:
            {
                var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var marker = list.IsOrdered ? $"{number++}. " : "• ";
                    var first = true;
                    foreach (var child in item)
                    {
                        AppendPlain(child, builder, first ? prefix + marker : prefix + new string(' ', marker.Length));
                        first = false;
                    }
                }

                break;
            }

            case MdTable table:
                foreach (var row in table.OfType<MdTableRow>())
                {
                    var cells = row.OfType<MdTableCell>().Select(c => string.Join(" ", c.OfType<ParagraphBlock>().Select(p => InlineText(p.Inline))));
                    builder.AppendLine(string.Join("\t", cells));
                }

                builder.AppendLine();
                break;
            case ThematicBreakBlock:
                builder.AppendLine("* * *").AppendLine();
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    AppendPlain(child, builder, prefix);
                }

                break;
        }
    }

    private static string InlineText(ContainerInline? container)
    {
        var builder = new StringBuilder();
        for (MdInline? inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.ToString());
                    break;
                case LinkInline link when link.IsImage:
                    break;
                case ContainerInline child:
                    builder.Append(InlineText(child));
                    break;
            }
        }

        return builder.ToString();
    }

    private sealed class RenderContext(ScriptStyle options)
    {
        public ScriptStyle Options { get; } = options;

        /// <summary>Si todo el texto ya va en negrita, el enfasis necesita un grosor mayor para distinguirse.</summary>
        public FontWeight Bold { get; } = options.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight()
            ? FontWeights.Black
            : FontWeights.Bold;

        public TextBlock CreateTextBlock(double size, Thickness margin)
        {
            return new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Options.FontFamily,
                FontSize = size,
                FontWeight = Options.FontWeight,
                Foreground = Options.Foreground,
                LineHeight = size * Options.LineSpacing,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = Options.Alignment,
                Margin = margin,
            };
        }

        public Run CreateRun(string text, bool bold, bool italic)
        {
            var run = new Run(text);
            if (bold)
            {
                run.FontWeight = Bold;
            }

            if (italic)
            {
                run.FontStyle = FontStyles.Italic;
            }

            return run;
        }
    }
}
