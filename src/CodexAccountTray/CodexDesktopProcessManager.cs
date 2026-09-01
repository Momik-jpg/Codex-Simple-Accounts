using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CodexAccountTray;

public interface ICodexDesktopRuntime
{
    bool IsRunning { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Launch();
}

public sealed class CodexDesktopRuntime : ICodexDesktopRuntime
{
    private const string PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    private const string AppUserModelId = PackageFamilyName + "!App";

    public bool IsRunning
    {
        get
        {
            Process[] processes = FindCodexProcesses();
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

    public void Launch()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"shell:AppsFolder\\{AppUserModelId}");
        Process.Start(startInfo)?.Dispose();
    }

    private static Process[] FindCodexProcesses()
    {
        return Process.GetProcessesByName("ChatGPT")
            .Where(IsCodexPackageProcess)
            .ToArray();
    }

    private static bool IsCodexPackageProcess(Process process)
    {
        try
        {
            uint length = 0;
            int result = GetPackageFamilyName(process.Handle, ref length, null);
            if (result != 122 || length == 0)
            {
                return false;
            }

            var family = new StringBuilder((int)length);
            result = GetPackageFamilyName(process.Handle, ref length, family);
            return result == 0 && string.Equals(
                family.ToString(),
                PackageFamilyName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(
        nint process,
        ref uint packageFamilyNameLength,
        StringBuilder? packageFamilyName);
}

public sealed class CodexDesktopProcessManager : ICodexProcessManager
{
    private readonly AccountStore _store;
    private readonly ICodexDesktopRuntime _runtime;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private Task? _pendingObserver;

    public CodexDesktopProcessManager(
        AccountStore store,
        string codexHome,
        ICodexDesktopRuntime runtime)
    {
        _store = store;
        ActiveCodexHome = codexHome;
        _runtime = runtime;
        ActiveAccount = DetectActiveAccount();
        LastAccount = ActiveAccount;
    }

    public bool IsRunning => _switchGate.CurrentCount == 0;

    public int? ActiveAccount { get; private set; }

    public int? PendingAccount { get; private set; }

    public int? LastAccount { get; private set; }

    public string ActiveCodexHome { get; }

    public event EventHandler? ProcessExited;

    public async Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken)
    {
        if (!_store.IsLoggedIn(accountNumber))
        {
            throw new InvalidOperationException($"Konto {accountNumber} ist nicht angemeldet.");
        }
        if (!await _switchGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Ein Kontowechsel läuft bereits.");
        }

        try
        {
            if (ActiveAccount == accountNumber)
            {
                _runtime.Launch();
                return;
            }

            if (_runtime.IsRunning)
            {
                PendingAccount = accountNumber;
                _pendingObserver ??= ObserveManualCloseAsync();
                return;
            }

            await ActivateAsync(accountNumber, cancellationToken);
        }
        finally
        {
            _switchGate.Release();
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ObserveManualCloseAsync()
    {
        try
        {
            await _runtime.WaitForExitAsync(CancellationToken.None);
            await _switchGate.WaitAsync();
            try
            {
                if (PendingAccount is int accountNumber)
                {
                    await ActivateAsync(accountNumber, CancellationToken.None);
                    PendingAccount = null;
                }
            }
            finally
            {
                _pendingObserver = null;
                _switchGate.Release();
                ProcessExited?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            PendingAccount = null;
            _pendingObserver = null;
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ActivateAsync(int accountNumber, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ActiveCodexHome);
        string authFile = Path.Combine(ActiveCodexHome, "auth.json");

        if (ActiveAccount is int previousAccount && File.Exists(authFile))
        {
            byte[] currentCredential = await File.ReadAllBytesAsync(authFile, cancellationToken);
            try
            {
                _store.Save(previousAccount, currentCredential);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentCredential);
            }
        }

        string temporary = authFile + ".codex-konten.tmp";
        byte[] credential = _store.Load(accountNumber);
        try
        {
            await File.WriteAllBytesAsync(temporary, credential, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }
        File.Move(temporary, authFile, true);

        ActiveAccount = accountNumber;
        LastAccount = accountNumber;
        _runtime.Launch();
    }

    private int? DetectActiveAccount()
    {
        string authFile = Path.Combine(ActiveCodexHome, "auth.json");
        if (!File.Exists(authFile))
        {
            return null;
        }

        byte[] active = File.ReadAllBytes(authFile);
        try
        {
            string? activeAccountId = ReadAccountId(active);
            foreach (int account in Enumerable.Range(1, 4).Where(_store.IsLoggedIn))
            {
                byte[] stored = _store.Load(account);
                try
                {
                    string? storedAccountId = ReadAccountId(stored);
                    if (activeAccountId is not null && storedAccountId == activeAccountId ||
                        activeAccountId is null && stored.SequenceEqual(active))
                    {
                        return account;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(stored);
                }
            }
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(active);
        }
    }

    private static string? ReadAccountId(byte[] auth)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(auth);
            return document.RootElement
                .GetProperty("tokens")
                .GetProperty("account_id")
                .GetString();
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
