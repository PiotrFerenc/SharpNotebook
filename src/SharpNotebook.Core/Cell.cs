namespace SharpNotebook.Core;

public enum CellType
{
    Code,
    Markdown,
    Ai,
}

/// <summary>One piece of cell output. MimeType is "text/plain", "text/html", or "image/png" (Data then base64).</summary>
public sealed record CellOutput(string MimeType, string Data);

public sealed class Cell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CellType Type { get; set; } = CellType.Code;
    public string Source { get; set; } = "";
    public List<CellOutput> Outputs { get; set; } = new();
    public int? ExecutionCount { get; set; }

    /// <summary>
    /// Freeform, but two carry behavior in the Desktop frontend: "hide-input" auto-collapses the source
    /// when the cell renders, "skip-on-run-all" is skipped by Restart+Run All / Run Above / Run Below.
    /// Missing on older files — defaults to empty, same pattern as <see cref="Notebook.Trusted"/>.
    /// </summary>
    public List<string> Tags { get; set; } = new();
}
