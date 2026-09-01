namespace SharpNotebook.Core;

public sealed class Notebook
{
    public int FormatVersion { get; set; } = 1;
    public List<Cell> Cells { get; set; } = new();

    /// <summary>
    /// True only for notebooks this app created itself (set by <see cref="NotebookFile.CreateEmpty"/>).
    /// A file that predates this flag, or was authored/edited elsewhere, deserializes with this false —
    /// System.Text.Json leaves a missing bool property at its default — so it's untrusted by default.
    /// </summary>
    public bool Trusted { get; set; }
}
