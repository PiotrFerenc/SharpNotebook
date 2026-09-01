using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SharpNotebook.Kernel.Host;

/// <summary>
/// Mirrors ScriptSession's ScriptState chain as a chain of Roslyn "submission" projects in an
/// AdhocWorkspace, purely so CompletionService has something to run against. Each successfully
/// executed cell commits a permanent submission project (referencing the previous one, exactly
/// like ScriptState.ContinueWithAsync chains); each completion request adds one throwaway
/// submission project for the in-progress cell and removes it again afterward.
/// </summary>
internal sealed class CompletionWorkspace
{
    private readonly AdhocWorkspace _workspace = new();
    private readonly CSharpCompilationOptions _compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithUsings(ScriptEnvironment.Imports)
        .WithScriptClassName("Submission");
    private readonly CSharpParseOptions _parseOptions = new(kind: SourceCodeKind.Script);
    private readonly List<MetadataReference> _references = [.. ScriptEnvironment.MetadataReferences];
    private ProjectId? _lastCommittedProjectId;

    /// <summary>Call after a cell executes successfully so later completions see its variables/usings.</summary>
    public void CommitCell(string code) => _lastCommittedProjectId = AddSubmissionProject(code);

    /// <summary>Call when a cell's `#r "nuget:..."` resolves new assemblies, so completion can see their types too.</summary>
    public void AddReferences(IEnumerable<MetadataReference> references) => _references.AddRange(references);

    public async Task<IReadOnlyList<string>> GetCompletionsAsync(string code, int position)
    {
        var projectId = AddSubmissionProject(code);
        try
        {
            var document = _workspace.CurrentSolution.GetProject(projectId)!.Documents.Single();
            var service = CompletionService.GetService(document);
            if (service is null)
                return [];

            var text = await document.GetTextAsync();
            var trigger = position > 0 && text[position - 1] == '.'
                ? CompletionTrigger.CreateInsertionTrigger('.')
                : CompletionTrigger.Invoke;
            var completions = await service.GetCompletionsAsync(document, position, trigger);

            // CompletionService returns every applicable symbol regardless of what's already typed (a real
            // editor's own filter-as-you-type does the narrowing); we have no editor doing that, so filter
            // by the identifier prefix immediately before the cursor ourselves.
            var prefixStart = position;
            while (prefixStart > 0 && (char.IsLetterOrDigit(text[prefixStart - 1]) || text[prefixStart - 1] == '_'))
                prefixStart--;
            var prefix = text.ToString(TextSpan.FromBounds(prefixStart, position));

            var items = completions.ItemsList.Select(i => i.DisplayText).Distinct();
            if (prefix.Length > 0)
                items = items.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            return items.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
        finally
        {
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(projectId));
        }
    }

    private ProjectId AddSubmissionProject(string code)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: projectId.ToString(),
            assemblyName: projectId.ToString(),
            language: LanguageNames.CSharp,
            compilationOptions: _compilationOptions,
            parseOptions: _parseOptions,
            metadataReferences: _references,
            projectReferences: _lastCommittedProjectId is { } prev ? [new ProjectReference(prev)] : null,
            isSubmission: true,
            hostObjectType: typeof(ScriptGlobals));

        var documentInfo = DocumentInfo.Create(
            documentId,
            "Submission.csx",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Create())));

        projectInfo = projectInfo.WithDocuments([documentInfo]);

        if (!_workspace.TryApplyChanges(_workspace.CurrentSolution.AddProject(projectInfo)))
            throw new InvalidOperationException("Failed to add submission project to completion workspace.");

        return projectId;
    }
}
