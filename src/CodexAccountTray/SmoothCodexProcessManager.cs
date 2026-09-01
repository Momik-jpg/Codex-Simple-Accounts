using System.Diagnostics;
using System.Security.Cryptography;

namespace CodexAccountTray;

public interface ISmoothSwitchControl
{
    bool ProxyActive { get; }
    bool HasActiveTurn { get; }
    Task EmergencyStopAsync(CancellationToken cancellationToken);
    Task LogoutAsync(int accountNumber, CancellationToken cancellationToken);
    void SynchronizeActiveAccount();
}

public interface IProxyDesktopRuntime
{
    bool IsRunning { get; }
    Task CloseAsync(CancellationToken cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Launch();
}

public sealed class ProxyDesktopRuntime : IProxyDesktopRuntime
{
    private const string AppUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    public bool IsRunning
    {
        get
        {
            Process[] processes = Process.GetProcessesByName("ChatGPT");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        while (IsRunning)
        {
            await Task.Delay(300, cancellationToken);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Process[] processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        using var graceful = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        graceful.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await WaitForExitAsync(graceful.Token);
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    // Every ChatGPT.exe instance is enumerated separately. Killing its full tree
                    // could also terminate Codex-Konten when Windows linked both launch chains.
                    process.Kill(entireProcessTree: false);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "ChatGPT-Hintergrundprozesse konnten nicht vollständig beendet werden. Der Wechsel wurde abgebrochen.");
        }
    }

    public void Launch()
    {
        var info = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.Environment.Remove("CODEX_APP_SERVER_WS_URL");
        info.ArgumentList.Add($"shell:AppsFolder\\{AppUserModelId}");
        Process.Start(info)?.Dispose();
    }
}

public sealed class SmoothCodexProcessManager : ICodexProcessManager, ISmoothSwitchControl, IDisposable
{
    private readonly AccountStore _store;
    private readonly IProxyDesktopRuntime _desktop;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _switchCancellation = new();

    public SmoothCodexProcessManager(
        AccountStore store,
        CodexCommand command,
        string codexHome,
        IProxyDesktopRuntime desktop)
    {
        _store = store;
        _desktop = desktop;
        ActiveCodexHome = codexHome;
        int? detected = _store.FindByAuthFile(Path.Combine(ActiveCodexHome, "auth.json"));
        ActiveAccount = detected;
        LastAccount = detected;
    }

    public bool IsRunning => _gate.CurrentCount == 0;
    public int? ActiveAccount { get; private set; }
    public int? PendingAccount { get; private set; }
    public int? LastAccount { get; private set; }
    public string ActiveCodexHome { get; }
    public bool ProxyActive => false;
    public bool HasActiveTurn => false;
    public event EventHandler? ProcessExited;

    public async Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken)
    {
        if (!_store.IsLoggedIn(accountNumber))
        {
            throw new InvalidOperationException($"Konto {accountNumber} ist nicht angemeldet.");
        }
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _switchCancellation.Token);
        CancellationToken switchToken = linkedCancellation.Token;
        if (!await _gate.WaitAsync(0, switchToken))
        {
            throw new InvalidOperationException("Ein Kontowechsel läuft bereits.");
        }
        try
        {
            if (_desktop.IsRunning && ActiveAccount == accountNumber)
            {
                return;
            }
            PendingAccount = accountNumber;
            try
            {
                if (_desktop.IsRunning)
                {
                    await _desktop.CloseAsync(switchToken);
                }
                await ActivateAsync(accountNumber, switchToken);
            }
            finally
            {
                PendingAccount = null;
            }
        }
        finally
        {
            _gate.Release();
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void SynchronizeActiveAccount()
    {
        int? detected = _store.FindByAuthFile(Path.Combine(ActiveCodexHome, "auth.json"));
        ActiveAccount = detected;
        LastAccount = detected;
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        _switchCancellation.Cancel();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PendingAccount = null;
            ActiveAccount = null;
            LegacyWebSocketCleanup.Remove();
            _switchCancellation.Dispose();
            _switchCancellation = new CancellationTokenSource();
        }
        finally
        {
            _gate.Release();
        }
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public async Task LogoutAsync(int accountNumber, CancellationToken cancellationToken)
    {
        bool removeActiveAuth = ActiveAccount == accountNumber;
        if (removeActiveAuth || PendingAccount == accountNumber)
        {
            await EmergencyStopAsync(cancellationToken);
            if (_desktop.IsRunning)
            {
                await _desktop.CloseAsync(cancellationToken);
            }
        }
        else
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _store.Remove(accountNumber);
            }
            finally
            {
                _gate.Release();
            }
            ProcessExited?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (removeActiveAuth)
        {
            string authFile = Path.Combine(ActiveCodexHome, "auth.json");
            if (File.Exists(authFile))
            {
                File.Delete(authFile);
            }
        }
        _store.Remove(accountNumber);
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        EmergencyStopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _switchCancellation.Dispose();
        _gate.Dispose();
    }

    private async Task ActivateAsync(int accountNumber, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ActiveCodexHome);
        string authFile = Path.Combine(ActiveCodexHome, "auth.json");
        int? previousAccount = ActiveAccount;
        if (ActiveAccount is int previous && File.Exists(authFile))
        {
            byte[] currentCredential = await File.ReadAllBytesAsync(authFile, cancellationToken);
            try
            {
                _store.Save(previous, currentCredential);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentCredential);
            }
        }
        string temporary = authFile + ".codex-konten.tmp";
        await WriteStoredCredentialAsync(accountNumber, temporary, cancellationToken);
        File.Move(temporary, authFile, true);
        try
        {
            _desktop.Launch();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(30));
            while (!_desktop.IsRunning)
            {
                await Task.Delay(250, startup.Token);
            }
        }
        catch (Exception exception)
        {
            if (previousAccount is int oldAccount)
            {
                await WriteStoredCredentialAsync(oldAccount, temporary, CancellationToken.None);
                File.Move(temporary, authFile, true);
            }
            throw new InvalidOperationException(
                "ChatGPT konnte nach dem Kontowechsel nicht normal gestartet werden.", exception);
        }

        int? verified = _store.FindByAuthFile(authFile);
        if (verified != accountNumber)
        {
            await RestorePreviousCredentialAsync(previousAccount, authFile, temporary);
            throw new InvalidOperationException("Das ausgewählte Konto konnte nicht verifiziert werden.");
        }
        ActiveAccount = accountNumber;
        LastAccount = accountNumber;
    }

    private async Task RestorePreviousCredentialAsync(
        int? previousAccount,
        string authFile,
        string temporary)
    {
        if (previousAccount is int oldAccount)
        {
            await WriteStoredCredentialAsync(oldAccount, temporary, CancellationToken.None);
            File.Move(temporary, authFile, true);
        }
        else if (File.Exists(authFile))
        {
            File.Delete(authFile);
        }
    }

    private async Task WriteStoredCredentialAsync(
        int accountNumber,
        string destination,
        CancellationToken cancellationToken)
    {
        byte[] credential = _store.Load(accountNumber);
        try
        {
            await File.WriteAllBytesAsync(destination, credential, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }
    }
}
