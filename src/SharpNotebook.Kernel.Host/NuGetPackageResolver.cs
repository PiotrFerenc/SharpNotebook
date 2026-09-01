using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SharpNotebook.Kernel.Host;

public sealed record NuGetResolveResult(bool Success, IReadOnlyList<Assembly> NewAssemblies, string? Error);

/// <summary>
/// Resolves `#r "nuget:..."` packages by shelling out to `dotnet restore` on a throwaway csproj instead of
/// reimplementing NuGet's dependency resolver (NuGet.Protocol/NuGet.Resolver) by hand — reuses the same
/// battle-tested resolution logic every .NET project already depends on. One resolver per notebook/kernel
/// process; every `#r nuget` in the session accumulates into the same csproj so later cells restore fast
/// (nothing new to add) and see packages requested by earlier cells.
/// </summary>
internal sealed class NuGetPackageResolver
{
    private readonly string _projectDir = Path.Combine(Path.GetTempPath(), "SharpNotebook-nuget-" + Guid.NewGuid());
    private readonly Dictionary<string, string> _requested = new(StringComparer.OrdinalIgnoreCase); // id -> version ("*" if unspecified)
    private readonly HashSet<string> _loadedPaths = new();

    public IReadOnlyDictionary<string, string> Requested => _requested;

    public async Task<NuGetResolveResult> ResolveAsync(IReadOnlyList<(string Id, string? Version)> packages)
    {
        var toAdd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, version) in packages)
        {
            var v = string.IsNullOrWhiteSpace(version) ? "*" : version;
            if (!_requested.TryGetValue(id, out var existing) || existing != v)
                toAdd[id] = v;
        }

        if (toAdd.Count == 0)
            return new NuGetResolveResult(true, [], null);

        Directory.CreateDirectory(_projectDir);
        var csprojPath = Path.Combine(_projectDir, "packages.csproj");
        // Write the tentative full set (existing + new) without committing `toAdd` to `_requested` yet —
        // if restore fails, a retry (e.g. after fixing a typo'd package id, or a transient network error)
        // must actually re-attempt it, not silently "succeed" because it looked already-requested.
        File.WriteAllText(csprojPath, BuildCsproj(_requested.Concat(toAdd).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)));

        var (exitCode, output) = await RunDotnetRestoreAsync(csprojPath);
        if (exitCode != 0)
            return new NuGetResolveResult(false, [], $"NuGet restore failed:\n{output}");

        foreach (var (id, v) in toAdd)
            _requested[id] = v;

        var newAssemblies = new List<Assembly>();
        foreach (var path in ParseAssetsFile())
        {
            if (!_loadedPaths.Add(path))
                continue;
            try
            {
                newAssemblies.Add(Assembly.LoadFrom(path));
            }
            catch
            {
                // native/resource-only assets can't be loaded as managed assemblies — skip them
            }
        }

        return new NuGetResolveResult(true, newAssemblies, null);
    }

    private static string BuildCsproj(Dictionary<string, string> packages)
    {
        var refs = string.Join('\n', packages.Select(kv => $"""    <PackageReference Include="{kv.Key}" Version="{kv.Value}" />"""));
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
            {refs}
              </ItemGroup>
            </Project>
            """;
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetRestoreAsync(string csprojPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"restore \"{csprojPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet restore.");
        // dotnet restore writes its actual NuGet errors (e.g. NU1101 package not found) to stdout, not stderr.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask + await stderrTask);
    }

    private List<string> ParseAssetsFile()
    {
        var assetsPath = Path.Combine(_projectDir, "obj", "project.assets.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = doc.RootElement;

        var packageFolders = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToList();
        var target = root.GetProperty("targets").EnumerateObject().First().Value;

        var paths = new List<string>();
        foreach (var library in target.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "package")
                continue;

            // "runtime" assets are what we can actually load and execute against; fall back to "compile"
            // for packages that only ship reference/analyzer assets under "runtime".
            if (!library.Value.TryGetProperty("runtime", out var assets) || assets.EnumerateObject().Any() is false)
                library.Value.TryGetProperty("compile", out assets);

            if (assets.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var asset in assets.EnumerateObject())
            {
                if (asset.Name.EndsWith("_._", StringComparison.Ordinal))
                    continue;

                var relative = Path.Combine(library.Name.ToLowerInvariant(), asset.Name);
                var absolute = packageFolders.Select(f => Path.Combine(f, relative)).FirstOrDefault(File.Exists);
                if (absolute is not null)
                    paths.Add(absolute);
            }
        }

        return paths;
    }
}
