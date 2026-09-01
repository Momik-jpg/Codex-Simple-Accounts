using System.Diagnostics;
using System.Security.Cryptography;

namespace CodexAccountTray;

public interface ICodexProcessManager
{
    bool IsRunning { get; }
    int? ActiveAccount { get; }
    int? PendingAccount { get; }
    int? LastAccount { get; }
    string ActiveCodexHome { get; }
    event EventHandler? ProcessExited;
    Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken);
}

public sealed class CodexProcessManager : ICodexProcessManager
{
    private readonly AppPaths _paths;
    private readonly AccountStore _store;
    private readonly CodexCommand _command;
    private readonly Func<string> _workingDirectory;
    private readonly IReadOnlyDictionary<string, string?> _extraEnvironment;
    private readonly bool _showConsole;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource _exitCompletion = NewCompletion();
    private Process? _process;
    private int? _activeAccount;

    public CodexProcessManager(
        AppPaths paths,
        AccountStore store,
        CodexCommand command,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null,
        bool showConsole = true)
        : this(paths, store, command, () => workingDirectory, extraEnvironment, showConsole)
    {
    }

    public CodexProcessManager(
        AppPaths paths,
        AccountStore store,
        CodexCommand command,
        Func<string> workingDirectory,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null,
        bool showConsole = true)
    {
        _paths = paths;
        _store = store;
        _command = command;
        _workingDirectory = workingDirectory;
        _extraEnvironment = extraEnvironment ?? new Dictionary<string, string?>();
        _showConsole = showConsole;
    }

    public bool IsRunning => _process is { HasExited: false };

    public int? ActiveAccount => _activeAccount;

    public int? PendingAccount => null;

    public int? LastAccount { get; private set; }

    public string ActiveCodexHome => _paths.SharedCodexHome;

    public event EventHandler? ProcessExited;

    public async Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Codex läuft bereits.");
            }
            if (!_store.IsLoggedIn(accountNumber))
            {
                throw new InvalidOperationException($"Konto {accountNumber} ist nicht angemeldet.");
            }

            _paths.EnsureCreated();
            string workingDirectory = _workingDirectory();
            Directory.CreateDirectory(workingDirectory);
            string authFile = Path.Combine(_paths.SharedCodexHome, "auth.json");
            byte[] credential = _store.Load(accountNumber);
            try
            {
                await File.WriteAllBytesAsync(authFile, credential, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credential);
            }

            var codexArguments = new List<string>();
            if (resumeLast)
            {
                codexArguments.AddRange(["resume", "--last", "--all"]);
            }

            string? wrapper = null;
            ProcessStartInfo startInfo;
            if (_showConsole)
            {
                wrapper = CommandWrapper.Create(
                    _paths,
                    _command,
                    workingDirectory,
                    _paths.SharedCodexHome,
                    codexArguments);
                startInfo = new ProcessStartInfo
                {
                    FileName = wrapper,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = _command.FileName,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string argument in _command.PrefixArguments.Concat(codexArguments))
                {
                    startInfo.ArgumentList.Add(argument);
                }
                startInfo.Environment["CODEX_HOME"] = _paths.SharedCodexHome;
                foreach ((string key, string? value) in _extraEnvironment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            _exitCompletion = NewCompletion();
            _activeAccount = accountNumber;
            LastAccount = accountNumber;
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Start();
            _ = ObserveExitAsync(_process, accountNumber, authFile, wrapper);
        }
        catch
        {
            _activeAccount = null;
            DeleteSharedAuth();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _exitCompletion.Task.WaitAsync(cancellationToken);
    }

    private async Task ObserveExitAsync(Process process, int accountNumber, string authFile, string? wrapper)
    {
        try
        {
            await process.WaitForExitAsync();
            if (File.Exists(authFile))
            {
                _store.Save(accountNumber, await File.ReadAllBytesAsync(authFile));
            }
        }
        finally
        {
            DeleteSharedAuth();
            if (wrapper is not null && File.Exists(wrapper))
            {
                File.Delete(wrapper);
            }
            process.Dispose();
            _process = null;
            _activeAccount = null;
            _exitCompletion.TrySetResult();
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DeleteSharedAuth()
    {
        string authFile = Path.Combine(_paths.SharedCodexHome, "auth.json");
        if (File.Exists(authFile))
        {
            File.Delete(authFile);
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
