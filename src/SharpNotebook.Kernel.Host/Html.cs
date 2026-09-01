namespace SharpNotebook.Kernel.Host;

/// <summary>Wrap a string in this to have it rendered as raw HTML instead of plain text — a bare string never is.</summary>
public readonly struct Html(string content)
{
    public string Content { get; } = content;
}
