using Bunit;
using SharpNotebook.Core;
using SharpNotebook.Web.Components.Pages;
using Xunit;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Since M4.5, cell editing is a real Monaco editor — its own completion popup is real Monaco UI with no
/// DOM bUnit can drive (no JS engine here), so the old dropdown this test used to exercise no longer
/// exists at all (removed from CellComponent). What's still ours to test is the kernel-calling half of the
/// wiring: <see cref="Home.BuildCompletionsAsync"/> (internal, exposed via InternalsVisibleTo) is the part
/// of the registered Monaco completion provider that doesn't depend on a live Monaco model — it takes the
/// cell text and cursor offset directly and calls the real kernel, exactly as the JS-facing half would
/// once Monaco supplies those two values.
/// </summary>
public class CompletionTests : WebTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task BuildCompletionsAsyncReturnsRealMemberCompletionsFromTheKernel()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "var myVariable = 5;" });
        var path = Path.Combine(root, "compdemo" + NotebookFile.Extension);
        NotebookFile.Save(path, notebook);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "compdemo").Click());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=cell-run]")), Timeout);

        // commit the variable into the kernel's ScriptState chain (same as executing that cell would)
        await cut.InvokeAsync(() => cut.Find("[data-testid=cell-run]").Click());
        cut.WaitForAssertion(() => Assert.Equal(1, NotebookFile.Load(path).Cells[0].ExecutionCount), Timeout);

        const string code = "myVariable.ToStr";
        var result = await cut.Instance.BuildCompletionsAsync(code, code.Length, lineNumber: 1, column: code.Length + 1);

        var labels = result.Suggestions!.Select(s => s.LabelAsString).ToList();
        Assert.Contains("ToString", labels);
        Assert.All(result.Suggestions!, s => Assert.NotNull(s.RangeAsObject));

        Directory.Delete(root, recursive: true);
    }
}
