using System.Text.Json;

namespace SharpNotebook.Core;

public static class NotebookFile
{
    public const string Extension = ".sharpnb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Notebook Load(string path) =>
        JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path), JsonOptions) ?? new Notebook();

    public static void Save(string path, Notebook notebook) =>
        File.WriteAllText(path, JsonSerializer.Serialize(notebook, JsonOptions));

    public static Notebook CreateEmpty()
    {
        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell());
        return notebook;
    }
}
