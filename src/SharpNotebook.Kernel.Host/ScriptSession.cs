using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace SharpNotebook.Kernel.Host;

public sealed record ExecutionResult(int ExecutionCount, bool Success, string? ErrorMessage, string? StackTrace);

/// <summary>Holds one Roslyn ScriptState chain — cells share variables/usings across executions, like a REPL.</summary>
public sealed class ScriptSession
{
    private readonly ScriptGlobals _globals = new();
    private readonly CompletionWorkspace _completions = new();
    private readonly NuGetPackageResolver _nuget = new();
    private ScriptOptions _options = ScriptOptions.Default
        .WithReferences(ScriptEnvironment.ReferenceAssemblies)
        .WithImports(ScriptEnvironment.Imports);
    private ScriptState<object>? _state;
    private int _executionCount;

    public async Task<ExecutionResult> ExecuteAsync(string code, Action<string> onOutput, Action<string, string> onDisplay)
    {
        _executionCount++;

        var (strippedCode, packages) = NuGetDirective.Extract(code);
        if (packages.Count > 0)
        {
            onOutput($"Installing NuGet package(s): {string.Join(", ", packages.Select(p => p.Id))}...\n");
            var resolved = await _nuget.ResolveAsync(packages);
            if (!resolved.Success)
                return new ExecutionResult(_executionCount, false, resolved.Error, null);

            if (resolved.NewAssemblies.Count > 0)
            {
                var newRefs = resolved.NewAssemblies.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToList();
                _options = _options.WithReferences(_options.MetadataReferences.Concat(newRefs));
                _completions.AddReferences(newRefs);
            }
        }

        var originalOut = Console.Out;
        Console.SetOut(new EventStreamTextWriter(onOutput));
        _globals.OnDisplay = onDisplay;
        try
        {
            _state = _state is null
                ? await CSharpScript.RunAsync<object>(strippedCode, _options, _globals)
                : await _state.ContinueWithAsync<object>(strippedCode, _options);

            if (_state.ReturnValue is not null)
                onDisplay(DisplayFormatter.MimeType(_state.ReturnValue), DisplayFormatter.Format(_state.ReturnValue));

            _completions.CommitCell(strippedCode);
            return new ExecutionResult(_executionCount, true, null, null);
        }
        catch (CompilationErrorException ex)
        {
            return new ExecutionResult(_executionCount, false, string.Join(Environment.NewLine, ex.Diagnostics), null);
        }
        catch (Exception ex)
        {
            return new ExecutionResult(_executionCount, false, ex.Message, ex.StackTrace);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    public Task<IReadOnlyList<string>> GetCompletionsAsync(string code, int position) =>
        _completions.GetCompletionsAsync(code, position);

    public Task<IReadOnlyList<(int Line, int Column, string Severity, string Message)>> GetDiagnosticsAsync(string code) =>
        _completions.GetDiagnosticsAsync(code);

    public Task<string?> GetHoverAsync(string code, int position) =>
        _completions.GetHoverAsync(code, position);

    // Roslyn's ScriptState.Variables accumulates one entry per declaration across the whole chained
    // submission history — a variable re-declared in a later cell (`var x = 1;` then `var x = 2;`) shows
    // up twice, in execution order. Keep only the last (current) value per name.
    public IReadOnlyList<(string Name, string Type, string Value)> GetVariables()
    {
        if (_state is null)
            return [];

        return _state.Variables
            .GroupBy(v => v.Name)
            .Select(g => g.Last())
            .Select(v => (v.Name, v.Type.Name, FormatValue(v.Value)))
            .ToList();
    }

    public IReadOnlyDictionary<string, string> GetPackages() => _nuget.Requested;

    private static string FormatValue(object? value)
    {
        try
        {
            return value?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"<ToString() threw: {ex.Message}>";
        }
    }
}
