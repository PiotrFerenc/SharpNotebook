using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace SharpNotebook.Kernel.Host;

/// <summary>The reference assemblies / default usings shared by both execution (ScriptSession) and completion (CompletionWorkspace) — keeping them identical is what makes IntelliSense match what actually runs.</summary>
internal static class ScriptEnvironment
{
    public static readonly string[] Imports =
        ["System", "System.Linq", "System.Collections.Generic", "System.Console", "SharpNotebook.Kernel.Host", "ScottPlot"];

    // Assembly.LoadFrom(path) on System.Private.CoreLib's own TPA path throws (corlib is bound by the
    // runtime before any user code runs, so re-"loading" it that way fails) — BuildReferenceAssemblies's
    // catch-all swallows that, so corlib silently never made it into the list at all. CSharpScript
    // execution has its own fallback reference resolver and never noticed; Roslyn's workspace/completion
    // compilation has no such fallback and failed every predefined-type lookup ("CS0518: Predefined type
    // 'System.Object' is not defined"). Fix: reference the already-loaded corlib directly instead of
    // trying to load it from disk again. Dedup by simple name (not object identity) since the TPA list
    // can list the same assembly twice under different paths.
    public static IReadOnlyList<Assembly> ReferenceAssemblies { get; } =
        BuildReferenceAssemblies().Prepend(typeof(object).Assembly).Append(typeof(Html).Assembly)
            .GroupBy(a => a.GetName().Name)
            .Select(g => g.First())
            .ToList();

    // Built via ScriptOptions (not a hand-rolled MetadataReference.CreateFromFile per assembly) — matches
    // exactly what CSharpScript itself resolves at execution time. Used for both real execution
    // (ScriptSession) and completion (CompletionWorkspace) — same references, same symbols in scope.
    public static ImmutableArray<MetadataReference> MetadataReferences { get; } =
        ScriptOptions.Default.WithReferences(ReferenceAssemblies).MetadataReferences;

    // .NET Core's CSharpScript has no implicit framework references, unlike desktop CSharpScript —
    // load the app's own trusted platform assemblies so scripts can use BCL types (LINQ, collections, etc.).
    private static IEnumerable<Assembly> BuildReferenceAssemblies()
    {
        var tpaList = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpaList))
        {
            yield return typeof(object).Assembly;
            yield return typeof(Console).Assembly;
            yield return typeof(Enumerable).Assembly;
            yield break;
        }

        foreach (var path in tpaList.Split(Path.PathSeparator))
        {
            Assembly? assembly = null;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch
            {
                // native/resource-only entries in the TPA list can't be loaded as managed assemblies — skip them
            }

            if (assembly is not null)
                yield return assembly;
        }
    }
}
