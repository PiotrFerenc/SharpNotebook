using System.Diagnostics;
using System.Runtime.CompilerServices;
using SharpNotebook.Kernel.Contracts;

namespace SharpNotebook.Services;

public readonly record struct CellRunResult(int ExecutionCount, bool Success);

/// <summary>Owns one kernel process (spawned per open notebook) and its stdin/stdout IPC.</summary>
public sealed class NotebookSession : IAsyncDisposable
{
    private Process? _process;
    // The kernel processes one request at a time anyway (its own read loop is sequential); this just
    // makes sure two overlapping calls here (e.g. a completion request fired while a cell is running)
    // don't read the same stdout stream concurrently, which throws.
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public Task StartAsync()
    {
        var dllPath = FindKernelHostDll();
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start kernel process.");
        return Task.CompletedTask;
    }

    public async Task SendAsync(KernelRequest request)
    {
        var stdin = _process?.StandardInput ?? throw new InvalidOperationException("Kernel not started.");
        await stdin.WriteLineAsync(Protocol.Serialize(request));
        await stdin.FlushAsync();
    }

    public async IAsyncEnumerable<KernelEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var stdout = _process?.StandardOutput ?? throw new InvalidOperationException("Kernel not started.");
        while (await stdout.ReadLineAsync(ct) is { } line)
        {
            if (Protocol.DeserializeEvent(line) is { } evt)
                yield return evt;
        }
    }

    /// <summary>Runs one cell and streams its output/error via callbacks until execution completes.</summary>
    public async Task<CellRunResult> RunCellAsync(string cellId, string code, Action<string, string> onDisplay, Action<string> onError)
    {
        await _ioLock.WaitAsync();
        try
        {
            await SendAsync(new ExecuteRequest(cellId, code));

            await foreach (var evt in ReadEventsAsync())
            {
                switch (evt)
                {
                    case OutputDisplayEvent display when display.CellId == cellId:
                        onDisplay(display.MimeType, display.Data);
                        break;
                    case OutputErrorEvent error when error.CellId == cellId:
                        onError(error.Message + (error.StackTrace is null ? "" : $"\n{error.StackTrace}"));
                        break;
                    case ExecutionCompletedEvent completed when completed.CellId == cellId:
                        return new CellRunResult(completed.ExecutionCount, completed.Success);
                }
            }

            return new CellRunResult(0, false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetCompletionsAsync(string cellId, string code, int position)
    {
        await _ioLock.WaitAsync();
        try
        {
            await SendAsync(new CompletionRequest(cellId, code, position));

            await foreach (var evt in ReadEventsAsync())
            {
                if (evt is CompletionResultEvent result && result.CellId == cellId)
                    return result.Items;
            }

            return [];
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetVariablesAsync()
    {
        await _ioLock.WaitAsync();
        try
        {
            await SendAsync(new VariablesRequest("variables"));

            await foreach (var evt in ReadEventsAsync())
            {
                if (evt is VariablesResultEvent result)
                    return result.Items;
            }

            return [];
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<IReadOnlyList<PackageInfo>> GetPackagesAsync()
    {
        await _ioLock.WaitAsync();
        try
        {
            await SendAsync(new PackagesRequest("packages"));

            await foreach (var evt in ReadEventsAsync())
            {
                if (evt is PackagesResultEvent result)
                    return result.Items;
            }

            return [];
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Kills the kernel process and spawns a fresh one — the only way to stop a stuck cell (Roslyn
    /// Scripting has no mid-execution cancellation) and what "restart kernel" means: a clean process with
    /// no variables from before. Any call blocked in RunCellAsync/GetCompletionsAsync against the killed
    /// process unblocks on its own (its stdout pipe closes) and releases <see cref="_ioLock"/> normally —
    /// this method doesn't need to (and shouldn't try to) wait for that first.
    /// </summary>
    public async Task RestartAsync()
    {
        var old = _process;
        _process = null;
        if (old is not null)
        {
            try
            {
                old.Kill(entireProcessTree: true);
            }
            catch
            {
                // already exited — fine
            }
            old.Dispose();
        }

        await StartAsync();
    }

    private static string FindKernelHostDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.sln").Length == 0 && dir.GetFiles("*.slnx").Length == 0)
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate solution root from " + AppContext.BaseDirectory);

        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);

        var dllPath = Path.Combine(dir.FullName, "src", "SharpNotebook.Kernel.Host", "bin", config, tfm, "SharpNotebook.Kernel.Host.dll");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Kernel host not built. Run 'dotnet build' on the solution first. Expected: {dllPath}");
        return dllPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
            return;

        try
        {
            await SendAsync(new ShutdownRequest());
            if (!_process.WaitForExit(2000))
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            _process.Kill(entireProcessTree: true);
        }
        finally
        {
            _process.Dispose();
        }
    }
}
