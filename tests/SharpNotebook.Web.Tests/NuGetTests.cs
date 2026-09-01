using Bunit;
using SharpNotebook.Core;
using SharpNotebook.Web.Components.Pages;
using Xunit;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Drives the real Home + CellComponent tree to prove M5's `#r "nuget:..."` support end-to-end: a package
/// resolved via a real `dotnet restore` in one cell stays referenced (and usable) in a later cell.
///
/// Cell content is pre-authored into the .sharpnb file rather than typed via simulated DOM events —
/// see the note on HomeTests for why (Monaco has no real DOM bUnit can drive).
/// </summary>
public class NuGetTests : WebTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task NuGetPackageResolvesAndStaysReferencedAcrossCells()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell
        {
            Type = CellType.Code,
            Source = "#r \"nuget:Newtonsoft.Json,13.0.3\"\nusing Newtonsoft.Json;",
        });
        notebook.Cells.Add(new Cell
        {
            Type = CellType.Code,
            Source = "JsonConvert.SerializeObject(new { hello = \"world\" })",
        });
        var path = Path.Combine(root, "nugetdemo" + NotebookFile.Extension);
        NotebookFile.Save(path, notebook);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "nugetdemo").Click());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=cell-run]").Count), Timeout);

        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[0].Click());
        cut.WaitForAssertion(() => Assert.DoesNotContain("ERROR", cut.FindAll("[data-testid=cell-output]")[0].TextContent), Timeout);

        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[1].Click());
        cut.WaitForAssertion(
            () => Assert.Contains("hello", cut.FindAll("[data-testid=cell-output]")[1].TextContent),
            Timeout);

        Directory.Delete(root, recursive: true);
    }
}
