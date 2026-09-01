using Bunit;
using SharpNotebook.Core;
using SharpNotebook.Web.Components.Pages;
using Xunit;

namespace SharpNotebook.Web.Tests;

/// <summary>
/// Drives the real Home + CellComponent tree to prove M3's rich display protocol end-to-end:
/// a returned IEnumerable renders as an HTML table, an explicit Display(Html) renders raw HTML,
/// and a ScottPlot chart (returned as PNG bytes) renders as an inline &lt;img&gt;.
///
/// Cell content is pre-authored into the .sharpnb file rather than typed via simulated DOM events —
/// see the note on HomeTests for why (Monaco has no real DOM bUnit can drive).
/// </summary>
public class RichOutputTests : WebTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CellOutputsRenderAsTableHtmlAndImage()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpNotebookTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        var notebook = new Notebook { Trusted = true };
        notebook.Cells.Add(new Cell
        {
            Type = CellType.Code,
            Source = "new[] { new { Name = \"Ala\", Age = 3 }, new { Name = \"Ola\", Age = 5 } }",
        });
        notebook.Cells.Add(new Cell { Type = CellType.Code, Source = "Display(new Html(\"<b>bold</b>\"))" });
        notebook.Cells.Add(new Cell
        {
            Type = CellType.Code,
            Source = "var plt = new Plot(); plt.Add.Signal(new double[] { 1, 3, 2, 5, 4 }); "
                   + "plt.GetImage(400, 300).GetImageBytes(ImageFormat.Png, 100)",
        });
        var path = Path.Combine(root, "richdemo" + NotebookFile.Extension);
        NotebookFile.Save(path, notebook);

        var cut = Render<Home>(p => p.Add(x => x.NotebooksRootDirectory, root));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "richdemo").Click());
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=cell-run]").Count), Timeout);

        // IEnumerable of objects -> HTML table with column headers and row values
        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[0].Click());
        cut.WaitForAssertion(() =>
        {
            var tables = cut.FindAll("[data-testid=cell-output] table");
            Assert.Single(tables);
            Assert.Contains("Ala", tables[0].TextContent);
            Assert.Contains("Ola", tables[0].TextContent);
        }, Timeout);

        // explicit Display(Html(...)) -> raw HTML, not escaped text
        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[1].Click());
        cut.WaitForAssertion(() =>
        {
            var bold = cut.FindAll("[data-testid=cell-output] b");
            Assert.Single(bold);
            Assert.Equal("bold", bold[0].TextContent);
        }, Timeout);

        // ScottPlot chart returned as PNG bytes -> inline <img data:image/png;base64,...>
        await cut.InvokeAsync(() => cut.FindAll("[data-testid=cell-run]")[2].Click());
        cut.WaitForAssertion(() =>
        {
            var images = cut.FindAll("[data-testid=cell-output] img");
            Assert.Single(images);
            Assert.StartsWith("data:image/png;base64,", images[0].GetAttribute("src"));
        }, Timeout);

        Directory.Delete(root, recursive: true);
    }
}
