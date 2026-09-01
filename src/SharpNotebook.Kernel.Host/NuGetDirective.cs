using System.Text.RegularExpressions;

namespace SharpNotebook.Kernel.Host;

/// <summary>Extracts <c>#r "nuget:PackageId,Version"</c> directives — Roslyn's own #r only understands file paths, not the "nuget:" scheme .NET Interactive popularized.</summary>
internal static partial class NuGetDirective
{
    [GeneratedRegex("""^\s*#r\s+"nuget:\s*([^,"]+?)\s*(?:,\s*([^"]+?)\s*)?"\s*$""", RegexOptions.Multiline)]
    private static partial Regex Pattern();

    public static (string Code, List<(string Id, string? Version)> Packages) Extract(string code)
    {
        var packages = new List<(string, string?)>();
        var stripped = Pattern().Replace(code, m =>
        {
            packages.Add((m.Groups[1].Value.Trim(), m.Groups[2].Success ? m.Groups[2].Value.Trim() : null));
            return "";
        });
        return (stripped, packages);
    }
}
