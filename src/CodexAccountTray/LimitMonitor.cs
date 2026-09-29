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
    private int? _waitingForCloseAccount;
    private bool _reportedNoAlternative;

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
    }

    public IReadOnlyDictionary<int, AccountLimits> Current => _current;
    public int? WaitingForCloseAccount => _waitingForCloseAccount;

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
        await _refreshGate.WaitAsync(cancellationToken);

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

            await ApplySwitchRuleAsync(cancellationToken);
            LimitsUpdated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(cancellationToken);
                if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Notice?.Invoke(this, $"Limitprüfung fehlgeschlagen; neuer Versuch in Kürze: {exception.Message}");
                try
                {
                    if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task<AccountLimits> ReadAccountAsync(int account, CancellationToken cancellationToken)
    {
        if (_processManager.ActiveAccount == account)
        {
            AccountLimits limits = await _protocol.ReadLimitsAsync(
                _processManager.ActiveCodexHome,
                cancellationToken);
            if (_processManager.IsRunning || _processManager.ActiveAccount != account)
                throw new InvalidOperationException("Das aktive Konto hat sich während der Limitprüfung geändert.");
            string activeAuth = Path.Combine(_processManager.ActiveCodexHome, "auth.json");
            await RefreshStoredCredentialAsync(account, activeAuth, cancellationToken);
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
            await RefreshStoredCredentialAsync(account, authFile, cancellationToken);
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

    private async Task RefreshStoredCredentialAsync(int account, string authFile, CancellationToken cancellationToken)
    {
        byte[] refreshed = await File.ReadAllBytesAsync(authFile, cancellationToken);
        try
        {
            byte[] stored = _store.Load(account);
            try
            {
                if (stored.AsSpan().SequenceEqual(refreshed)) return;
                string? expectedId = AccountProfileReader.ReadAccountId(stored);
                string? refreshedId = AccountProfileReader.ReadAccountId(refreshed);
                if (string.IsNullOrWhiteSpace(expectedId) ||
                    !string.Equals(expectedId, refreshedId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Die Kontoidentität hat sich während der Limitprüfung geändert.");
                _store.Save(account, refreshed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stored);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(refreshed);
        }
    }

    private async Task ApplySwitchRuleAsync(CancellationToken cancellationToken)
    {
        if (!_autoSwitchEnabled())
        {
            _waitingForCloseAccount = null;
            _reportedNoAlternative = false;
            return;
        }

        int? currentAccount = _processManager.ActiveAccount ?? _processManager.LastAccount;
        if (currentAccount is null ||
            !_current.TryGetValue(currentAccount.Value, out AccountLimits? currentLimits) ||
            currentLimits.IsStale ||
            !currentLimits.RequiresSwitch)
        {
            _waitingForCloseAccount = null;
            _reportedNoAlternative = false;
            return;
        }

        int? next = AccountSelector.NextAvailable(
            currentAccount.Value,
            _store.AccountNumbers.Select(account => new AccountAvailability(
                account,
                _store.IsLoggedIn(account),
                _current.TryGetValue(account, out AccountLimits? limits) && !limits.IsStale
                    ? limits.AvailablePrimaryPercent
                    : 0)));

        if (next is null)
        {
            _waitingForCloseAccount = null;
            if (!_reportedNoAlternative)
            {
                Notice?.Invoke(this, "Kein weiteres Konto mit verfügbarem 5-Stunden- und Wochenlimit.");
                _reportedNoAlternative = true;
            }
            return;
        }

        _reportedNoAlternative = false;

        if (_processManager is ISmoothSwitchControl control && control.IsDesktopRunning)
        {
            if (_waitingForCloseAccount != next)
                Notice?.Invoke(this, $"Limit erreicht. {_store.DisplayName(next.Value)} startet, sobald Codex geschlossen ist.");
            _waitingForCloseAccount = next;
            return;
        }

        if (_processManager.IsRunning) return;

        _waitingForCloseAccount = null;
        try
        {
            await _processManager.StartAsync(next.Value, resumeLast: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Notice?.Invoke(this,
                $"Automatischer Wechsel zu {_store.DisplayName(next.Value)} wurde nicht ausgeführt: {exception.Message}");
        }
    }

}
