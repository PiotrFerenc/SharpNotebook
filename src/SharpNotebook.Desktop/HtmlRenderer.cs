using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace SharpNotebook.Desktop;

/// <summary>
/// A small, deliberately narrow HTML renderer — not a browser engine. It covers exactly what this app's
/// own DisplayFormatter emits (well-formed &lt;table&gt;/&lt;tr&gt;/&lt;th&gt;/&lt;td&gt;) plus the common
/// inline tags a user's own Html(...) call is likely to contain (b/strong, i/em, br). Anything that
/// doesn't parse as XML (real-world arbitrary HTML often won't — unclosed tags, entities, etc.) falls back
/// to a tag-stripped plain-text render rather than failing outright.
/// </summary>
internal static class HtmlRenderer
{
    public static Control Render(string html, IBrush foreground)
    {
        XElement root;
        try
        {
            root = XDocument.Parse($"<root>{html}</root>").Root!;
        }
        catch
        {
            return PlainTextFallback(html, foreground);
        }

        var table = root.Descendants("table").FirstOrDefault();
        return table is not null ? RenderTable(table, foreground) : RenderInline(root, foreground);
    }

    private static Control RenderTable(XElement table, IBrush foreground)
    {
        var rows = table.Elements("tr").ToList();
        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Elements("th").Concat(r.Elements("td")).Count());

        var grid = new Grid();
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rows[r].Elements("th").Concat(rows[r].Elements("td")).ToList();
            for (var c = 0; c < cells.Count; c++)
            {
                var isHeader = cells[c].Name.LocalName == "th";
                var cellBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
                    BorderThickness = new Avalonia.Thickness(0, 0, 1, 1),
                    Padding = new Avalonia.Thickness(6, 3),
                    Child = new TextBlock
                    {
                        Text = cells[c].Value,
                        Foreground = foreground,
                        FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
                    },
                };
                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
            BorderThickness = new Avalonia.Thickness(1, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = grid,
        };
    }

    private static Control RenderInline(XElement root, IBrush foreground)
    {
        var textBlock = new TextBlock { Foreground = foreground, TextWrapping = TextWrapping.Wrap };
        AppendInlines(root, textBlock.Inlines!, bold: false, italic: false);
        return textBlock;
    }

    private static void AppendInlines(XElement element, InlineCollection inlines, bool bold, bool italic)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    inlines.Add(new Run(text.Value) { FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = italic ? FontStyle.Italic : FontStyle.Normal });
                    break;
                case XElement el when el.Name.LocalName is "br":
                    inlines.Add(new LineBreak());
                    break;
                case XElement el when el.Name.LocalName is "b" or "strong":
                    AppendInlines(el, inlines, bold: true, italic);
                    break;
                case XElement el when el.Name.LocalName is "i" or "em":
                    AppendInlines(el, inlines, bold, italic: true);
                    break;
                case XElement el:
                    AppendInlines(el, inlines, bold, italic);
                    break;
            }
        }
    }

    private static Control PlainTextFallback(string html, IBrush foreground) => new TextBlock
    {
        Text = Regex.Replace(html, "<[^>]+>", ""),
        Foreground = foreground,
        TextWrapping = TextWrapping.Wrap,
    };
}
