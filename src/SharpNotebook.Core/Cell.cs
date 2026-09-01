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
}
