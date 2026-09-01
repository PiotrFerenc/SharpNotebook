using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;

namespace SharpNotebook.Kernel.Host;

/// <summary>Turns a script's return value (or an explicit Display() argument) into a (mimeType, data) pair.</summary>
internal static class DisplayFormatter
{
    public static string MimeType(object? value) => value switch
    {
        Html => "text/html",
        byte[] => "image/png",
        string => "text/plain",
        IEnumerable => "text/html",
        _ => "text/plain",
    };

    public static string Format(object? value) => value switch
    {
        null => "",
        Html html => html.Content,
        byte[] bytes => Convert.ToBase64String(bytes),
        string s => s,
        IEnumerable seq => FormatTable(seq),
        _ => value.ToString() ?? "",
    };

    private static string FormatTable(IEnumerable seq)
    {
        var items = seq.Cast<object?>().ToList();
        if (items.Count == 0)
            return "<table><tr><td><em>(empty)</em></td></tr></table>";

        var firstType = items[0]?.GetType();
        var props = firstType is not null && firstType != typeof(string) && !firstType.IsPrimitive
            ? firstType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            : [];

        var sb = new StringBuilder("<table border=\"1\" style=\"border-collapse:collapse;\">");

        if (props.Length > 0)
        {
            sb.Append("<tr>");
            foreach (var p in props)
                sb.Append($"<th>{WebUtility.HtmlEncode(p.Name)}</th>");
            sb.Append("</tr>");

            foreach (var item in items)
            {
                sb.Append("<tr>");
                foreach (var p in props)
                    sb.Append($"<td>{WebUtility.HtmlEncode(p.GetValue(item)?.ToString() ?? "")}</td>");
                sb.Append("</tr>");
            }
        }
        else
        {
            foreach (var item in items)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(item?.ToString() ?? "")}</td></tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
