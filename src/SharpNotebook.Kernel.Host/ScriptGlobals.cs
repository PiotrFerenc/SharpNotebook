namespace SharpNotebook.Kernel.Host;

/// <summary>
/// Roslyn Scripting "globals" object — its public members are in scope, unqualified, inside every cell.
/// Lets a cell push extra output (e.g. mid-loop) instead of only the last expression's value.
/// </summary>
public sealed class ScriptGlobals
{
    internal Action<string, string> OnDisplay { get; set; } = (_, _) => { };

    public void Display(object? value) => OnDisplay(DisplayFormatter.MimeType(value), DisplayFormatter.Format(value));
}
