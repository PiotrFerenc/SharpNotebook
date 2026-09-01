using Bunit;
using SharpNotebook.Core;
using SharpNotebook.Web.Components.Pages;
using Xunit;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Drives the real Home + FileBrowser + CellComponent tree (spawns real Kernel.Host processes, real
/// Roslyn execution, real file I/O) exactly like a browser would — no browser required. Covers M2's
/// acceptance criterion: a notebook with 2 code cells + 1 markdown cell runs correctly and persists.
///
/// Cell content is authored directly into the .sharpnb file rather than "typed" via simulated DOM events:
/// since M4.5, cell source lives in a Monaco editor, which has no real DOM bUnit can drive (no JS engine).
/// RunAsync sends Cell.Source to the kernel regardless of what the (unrenderable, in this environment)
/// editor shows, so pre-authoring the file still exercises the real execution/persistence pipeline —
/// only "type into a cell live" is untestable here, see CLAUDE.md.
/// </summary>
public class HomeTests : WebTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task NotebookRunsAndPersistsAcrossReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "var x = 5; x" });
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "x + 1" });
        notebook.Cells.Add(new Cell { Type = CellType.Markdown, Source = "# hello" });
        var path = Path.Combine(root, "demo" + NotebookFile.Extension);
        NotebookFile.Save(path, notebook);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "demo").Click());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=cell-run]").Count), Timeout);

        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[0].Click());
        cut.WaitForAssertion(() => Assert.Contains("5", cut.FindAll("[data-testid=cell-output]")[0].TextContent), Timeout);

        // proves cross-cell state: cell 2's "x" only resolves if the kernel kept cell 1's variable
        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[1].Click());
        cut.WaitForAssertion(() => Assert.Contains("6", cut.FindAll("[data-testid=cell-output]")[1].TextContent), Timeout);

        // Persistence: re-read the file directly — the UI has no way to read Monaco's live buffer back
        // out. The autosave that follows a run happens *after* the output already became visible above
        // (RunAsync updates output via StateHasChanged mid-execution, then saves at the very end), so
        // this needs its own poll rather than a single read right after the DOM assertion.
        cut.WaitForAssertion(() =>
        {
            var reloaded = NotebookFile.Load(path);
            Assert.Equal(3, reloaded.Cells.Count);
            Assert.Equal(CellType.Markdown, reloaded.Cells[2].Type);
            Assert.Equal(1, reloaded.Cells[0].ExecutionCount);
            Assert.Equal(2, reloaded.Cells[1].ExecutionCount);
            Assert.Contains(reloaded.Cells[0].Outputs, o => o.Data.Contains('5'));
            Assert.Contains(reloaded.Cells[1].Outputs, o => o.Data.Contains('6'));
        }, Timeout);

        // reopening in a fresh component instance restores the right structure (3 cells, 2 runnable)
        var reopened = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await reopened.InvokeAsync(() => reopened.FindAll("button").First(b => b.TextContent.Trim() == "demo").Click());
        reopened.WaitForAssertion(() => Assert.Equal(2, reopened.FindAll("[data-testid=cell-run]").Count), Timeout);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task CreateNotebookButtonMakesAFreshOneCellNotebook()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));

        await cut.InvokeAsync(() => cut.Find("[data-testid=new-notebook-name]").Input("fresh"));
        await cut.InvokeAsync(() => cut.Find("[data-testid=create-notebook]").Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=cell-run]")), Timeout);

        var path = Directory.GetFiles(root, "*.sharpnb", SearchOption.AllDirectories).Single();
        var notebook = NotebookFile.Load(path);
        Assert.True(notebook.Trusted);
        Assert.Single(notebook.Cells);

        Directory.Delete(root, recursive: true);
    }
}
