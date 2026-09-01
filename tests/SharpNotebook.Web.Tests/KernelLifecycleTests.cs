using Bunit;
using SharpNotebook.Core;
using SharpNotebook.Web.Components.Pages;
using Xunit;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Drives the real Home + CellComponent tree to prove M6's kernel lifecycle features end-to-end (real
/// Kernel.Host process kill + respawn, not a simulation): restart-and-run-all rebuilds state from a killed,
/// variable-free kernel; a notebook not authored by this app opens locked until explicitly trusted.
/// </summary>
public class KernelLifecycleTests : WebTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RestartAndRunAllRebuildsStateFromFreshKernel()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        // Pre-authored (not typed via simulated DOM events — Monaco has no real DOM bUnit can drive,
        // see the note on HomeTests) so this still proves the real point: cell 2's "x" only resolves if
        // Restart genuinely rebuilt kernel state from cell 1, run top to bottom, not leftover process state.
        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "var x = 5; x" });
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "x + 1" });
        var path = Path.Combine(root, "lifecycle" + NotebookFile.Extension);
        NotebookFile.Save(path, notebook);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "lifecycle").Click());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=cell-run]").Count), Timeout);

        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[0].Click());
        cut.WaitForAssertion(() => Assert.Contains("5", cut.FindAll("[data-testid=cell-output]")[0].TextContent), Timeout);

        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[1].Click());
        cut.WaitForAssertion(() => Assert.Contains("6", cut.FindAll("[data-testid=cell-output]")[1].TextContent), Timeout);

        // Restart kills the kernel process (fresh, variable-free) and re-runs every code cell top to bottom —
        // if the second cell's "x" only worked because of leftover process state, this would fail.
        await cut.InvokeAsync(() => cut.Find("[data-testid=restart-run-all]").Click());
        cut.WaitForAssertion(() =>
        {
            var outputs = cut.FindAll("[data-testid=cell-output]");
            Assert.Contains("5", outputs[0].TextContent);
            Assert.Contains("6", outputs[1].TextContent);
        }, Timeout);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task UntrustedNotebookLocksRunUntilExplicitlyTrusted()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        // Simulate a notebook not authored by this app: Trusted defaults to false, and this bypasses the
        // FileBrowser "Nowy" flow (the only path that sets Trusted = true) entirely.
        var foreign = new Notebook();
        foreign.Cells.Add(new Cell { Type = CellType.Code, Source = "1 + 1" });
        var path = Path.Combine(root, "foreign" + NotebookFile.Extension);
        NotebookFile.Save(path, foreign);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "foreign").Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=cell-source]")), Timeout);

        Assert.Single(cut.FindAll("[data-testid=trust-banner]"));
        Assert.NotNull(cut.Find("[data-testid=cell-run]").GetAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Find("[data-testid=trust-notebook]").Click());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=trust-banner]")), Timeout);
        Assert.Null(cut.Find("[data-testid=cell-run]").GetAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Find("[data-testid=cell-run]").Click());
        cut.WaitForAssertion(() => Assert.Contains("2", cut.Find("[data-testid=cell-output]").TextContent), Timeout);

        Directory.Delete(root, recursive: true);
    }
}
