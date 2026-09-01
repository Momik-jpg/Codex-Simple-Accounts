using System.Security.Cryptography;

namespace CodexAccountTray;

public sealed class LimitMonitor : IDisposable
{
    private readonly AppPaths _paths;
    private readonly AccountStore _store;
    private readonly ICodexProtocolClient _protocol;
    private readonly ICodexProcessManager _processManager;
    private readonly TimeSpan _interval;
    private readonly Func<bool> _autoSwitchEnabled;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<int, AccountLimits> _current = [];
    private CancellationTokenSource? _runCancellation;
    private int? _pendingAccount;

    public LimitMonitor(
        AppPaths paths,
        AccountStore store,
        ICodexProtocolClient protocol,
        ICodexProcessManager processManager,
        TimeSpan interval,
        Func<bool>? autoSwitchEnabled = null)
    {
        _paths = paths;
        _store = store;
        _protocol = protocol;
        _processManager = processManager;
        _interval = interval;
        _autoSwitchEnabled = autoSwitchEnabled ?? (() => true);
        _processManager.ProcessExited += OnProcessExited;
    }

    public IReadOnlyDictionary<int, AccountLimits> Current => _current;

    public event EventHandler? LimitsUpdated;

    public event EventHandler<string>? Notice;

    public void Start()
    {
        if (_runCancellation is not null)
        {
            return;
        }

        _runCancellation = new CancellationTokenSource();
        _ = RunAsync(_runCancellation.Token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_processManager is ISmoothSwitchControl control)
            {
                control.SynchronizeActiveAccount();
            }
            foreach (int account in _store.AccountNumbers)
            {
                if (!_store.IsLoggedIn(account))
                {
                    _current.Remove(account);
                    continue;
                }

                try
                {
                    _current[account] = await ReadAccountAsync(account, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    if (_current.TryGetValue(account, out AccountLimits? previous))
                    {
                        _current[account] = previous with { IsStale = true };
                    }
                }
            }

            LimitsUpdated?.Invoke(this, EventArgs.Empty);
            await ApplySwitchRuleAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _processManager.ProcessExited -= OnProcessExited;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<AccountLimits> ReadAccountAsync(int account, CancellationToken cancellationToken)
    {
        if (_processManager.ActiveAccount == account)
        {
            AccountLimits limits = await _protocol.ReadLimitsAsync(
                _processManager.ActiveCodexHome,
                cancellationToken);
            string activeAuth = Path.Combine(_processManager.ActiveCodexHome, "auth.json");
            if (File.Exists(activeAuth))
            {
                _store.Save(account, await File.ReadAllBytesAsync(activeAuth, cancellationToken));
            }
            return limits;
        }

        string probeHome = Path.Combine(_paths.Root, "Probe", account.ToString());
        Directory.CreateDirectory(probeHome);
        string authFile = Path.Combine(probeHome, "auth.json");
        byte[] credential = _store.Load(account);
        try
        {
            await File.WriteAllBytesAsync(authFile, credential, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }
        try
        {
            AccountLimits limits = await _protocol.ReadLimitsAsync(probeHome, cancellationToken);
            if (File.Exists(authFile))
            {
                _store.Save(account, await File.ReadAllBytesAsync(authFile, cancellationToken));
            }
            return limits;
        }
        finally
        {
            if (File.Exists(authFile))
            {
                File.Delete(authFile);
            }
        }
    }

    private async Task ApplySwitchRuleAsync(CancellationToken cancellationToken)
    {
        if (!_autoSwitchEnabled())
        {
            return;
        }

        int? currentAccount = _processManager.ActiveAccount ?? _processManager.LastAccount;
        if (currentAccount is null ||
            !_current.TryGetValue(currentAccount.Value, out AccountLimits? currentLimits) ||
            !currentLimits.RequiresSwitch)
        {
            return;
        }

        int? next = AccountSelector.NextAvailable(
            currentAccount.Value,
            _store.AccountNumbers.Select(account => new AccountAvailability(
                account,
                _store.IsLoggedIn(account),
                _current.TryGetValue(account, out AccountLimits? limits)
                    ? limits.AvailablePrimaryPercent
                    : 0)));

        if (next is null)
        {
            Notice?.Invoke(this, "Kein weiteres Konto mit verfügbarem 5-Stunden- und Wochenlimit.");
            return;
        }

        if (_processManager.IsRunning)
        {
            _pendingAccount = next;
            Notice?.Invoke(this, $"{_store.DisplayName(next.Value)} startet nach dem Ende der laufenden Antwort.");
            return;
        }

        await _processManager.StartAsync(next.Value, resumeLast: true, cancellationToken);
    }

    private async void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        int? next = _pendingAccount;
        _pendingAccount = null;
        if (next is null)
        {
            return;
        }

        try
        {
            await _processManager.StartAsync(next.Value, resumeLast: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            Notice?.Invoke(this, $"Wechsel zu {_store.DisplayName(next.Value)} fehlgeschlagen: {exception.Message}");
        }
    }
}
