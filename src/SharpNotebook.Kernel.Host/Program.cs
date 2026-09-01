using SharpNotebook.Kernel.Contracts;
using SharpNotebook.Kernel.Host;

var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
var session = new ScriptSession();

void Send(KernelEvent evt) => stdout.WriteLine(Protocol.Serialize(evt));

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;

    var request = Protocol.DeserializeRequest(line);

    switch (request)
    {
        case ExecuteRequest exec:
            var result = await session.ExecuteAsync(
                exec.Code,
                text => Send(new OutputDisplayEvent(exec.CellId, "text/plain", text)),
                (mime, data) => Send(new OutputDisplayEvent(exec.CellId, mime, data)));

            if (!result.Success)
                Send(new OutputErrorEvent(exec.CellId, result.ErrorMessage ?? "", result.StackTrace));

            Send(new ExecutionCompletedEvent(exec.CellId, result.ExecutionCount, result.Success));
            break;
        case CompletionRequest comp:
            var items = await session.GetCompletionsAsync(comp.Code, comp.Position);
            Send(new CompletionResultEvent(comp.CellId, items.ToList()));
            break;
        case VariablesRequest vars:
            var variables = session.GetVariables().Select(v => new VariableInfo(v.Name, v.Type, v.Value)).ToList();
            Send(new VariablesResultEvent(vars.CellId, variables));
            break;
        case PackagesRequest pkgs:
            var packages = session.GetPackages().Select(kv => new PackageInfo(kv.Key, kv.Value)).ToList();
            Send(new PackagesResultEvent(pkgs.CellId, packages));
            break;
        case ShutdownRequest:
            return;
    }
}
