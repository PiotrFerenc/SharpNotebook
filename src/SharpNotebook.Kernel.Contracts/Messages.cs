using System.Text.Json.Serialization;

namespace SharpNotebook.Kernel.Contracts;

// There is deliberately no "interrupt" request: the kernel's stdin-read loop is blocked inside
// CSharpScript.RunAsync while a cell executes, so it can't be listening for an IPC message telling it to
// stop — interrupting a stuck cell can only happen from outside the process (NotebookSession.RestartAsync
// kills it). Roslyn Scripting has no cooperative mid-execution cancellation to hook up even if it could.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExecuteRequest), "execute")]
[JsonDerivedType(typeof(CompletionRequest), "completion")]
[JsonDerivedType(typeof(VariablesRequest), "variables")]
[JsonDerivedType(typeof(PackagesRequest), "packages")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
public abstract record KernelRequest;

public sealed record ExecuteRequest(string CellId, string Code) : KernelRequest;
public sealed record CompletionRequest(string CellId, string Code, int Position) : KernelRequest;
public sealed record VariablesRequest(string CellId) : KernelRequest;
public sealed record PackagesRequest(string CellId) : KernelRequest;
public sealed record ShutdownRequest : KernelRequest;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OutputDisplayEvent), "outputDisplay")]
[JsonDerivedType(typeof(OutputErrorEvent), "outputError")]
[JsonDerivedType(typeof(ExecutionCompletedEvent), "executionCompleted")]
[JsonDerivedType(typeof(CompletionResultEvent), "completionResult")]
[JsonDerivedType(typeof(VariablesResultEvent), "variablesResult")]
[JsonDerivedType(typeof(PackagesResultEvent), "packagesResult")]
public abstract record KernelEvent;

public sealed record CompletionResultEvent(string CellId, List<string> Items) : KernelEvent;
public sealed record VariableInfo(string Name, string Type, string Value);
public sealed record VariablesResultEvent(string CellId, List<VariableInfo> Items) : KernelEvent;
public sealed record PackageInfo(string Id, string Version);
public sealed record PackagesResultEvent(string CellId, List<PackageInfo> Items) : KernelEvent;

/// <summary>One piece of cell output. MimeType is "text/plain", "text/html", or "image/png" (Data then base64).</summary>
public sealed record OutputDisplayEvent(string CellId, string MimeType, string Data) : KernelEvent;
public sealed record OutputErrorEvent(string CellId, string Message, string? StackTrace) : KernelEvent;
public sealed record ExecutionCompletedEvent(string CellId, int ExecutionCount, bool Success) : KernelEvent;
