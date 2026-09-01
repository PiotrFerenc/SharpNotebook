using System.Text.Json;
using SharpNotebook.Core;

namespace SharpNotebook.Desktop;

/// <summary>A saved cell, reusable as a starting point for a new one. Global across all notebooks —
/// deliberately not per-notebook, since the point is reusing a snippet you liked in one notebook from
/// any other.</summary>
public sealed record CellTemplate(string Name, CellType Type, string Source);

internal static class FavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SharpNotebook", "favorites.json");

    public static List<CellTemplate> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<CellTemplate>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string path, IEnumerable<CellTemplate> favorites)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(favorites.ToList(), JsonOptions));
    }
}
