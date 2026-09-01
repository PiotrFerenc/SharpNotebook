using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpNotebook.Services;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Monaco (via BlazorMonaco) makes real JS calls to create/register itself — bUnit has no JS engine to
/// run them against, and its default Strict JSInterop mode throws on any unconfigured call. Loose mode
/// makes unconfigured calls return default(T) instead, which is enough for our C#-side logic (execution,
/// persistence, trust gating, kernel lifecycle) to keep running; it does not mean the editor itself is
/// exercised — that half is untestable here, see CLAUDE.md.
/// </summary>
public abstract class WebTestBase : BunitContext
{
    protected WebTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IAiCodeGenerator>(new StubAiCodeGenerator());
    }
}

/// <summary>No real network calls in tests — the AI cell type is exercised via WebTestBase-registered
/// components only insofar as CellComponent needs an IAiCodeGenerator to inject; nothing here asserts
/// on generated content.</summary>
file sealed class StubAiCodeGenerator : IAiCodeGenerator
{
    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default) => Task.FromResult("// stub");
}
