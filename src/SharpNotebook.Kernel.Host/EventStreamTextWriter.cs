using System.Text;

namespace SharpNotebook.Kernel.Host;

/// <summary>Forwards script Console output to a callback instead of the real process stdout (which carries the IPC protocol).</summary>
public sealed class EventStreamTextWriter(Action<string> onText) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => onText(value.ToString());

    public override void Write(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            onText(value);
    }

    public override void WriteLine(string? value) => onText((value ?? "") + Environment.NewLine);

    // Console.WriteLine(nonStringOverload) — e.g. Console.WriteLine(5) — calls the base TextWriter's
    // Write(value) then this parameterless WriteLine() for the trailing newline, as two separate calls.
    // Without this override, the newline falls through to the base's Write(char[]) default (looping
    // Write(char) once per character, one call for '\n' on Linux) — a *second*, separate OutputDisplayEvent
    // containing only a newline, which showed up as an empty output box under real output in the browser.
    public override void WriteLine() => onText(Environment.NewLine);
}
