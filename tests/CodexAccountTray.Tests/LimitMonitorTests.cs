using System.Text;
using System.Text.Json;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class LimitMonitorTests
{
    [Fact]
    public async Task RefreshAsync_SwitchesToNextAvailableAccountAtOnePercent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 1, [2] = 80 });
        var process = new FakeProcessManager { LastAccount = 1 };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => true);

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(2, process.StartedAccount);
            Assert.True(process.ResumedLast);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_DoesNotSwitchWhenAutoSwapIsOff()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 1, [2] = 80 });
        var process = new FakeProcessManager { LastAccount = 1 };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => false);

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);
            Assert.Null(process.StartedAccount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_SwitchesWhenWeeklyLimitIsEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(
            new Dictionary<int, int> { [1] = 80, [2] = 80 },
            new Dictionary<int, int> { [1] = 0, [2] = 80 });
        var process = new FakeProcessManager { LastAccount = 1 };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => true);

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(2, process.StartedAccount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_SkipsAccountWithEmptyWeeklyLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        store.Save(3, Encoding.UTF8.GetBytes("account-3"));
        var protocol = new FakeProtocolClient(
            new Dictionary<int, int> { [1] = 80, [2] = 80, [3] = 70 },
            new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 50 });
        var process = new FakeProcessManager { LastAccount = 1 };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => true);

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(3, process.StartedAccount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_KeepsProbeCacheButRemovesPlaintextCredential()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 80 });
        var process = new FakeProcessManager { LastAccount = 1 };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => false);

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            string probeHome = Path.Combine(root, "Probe", "1");
            Assert.True(Directory.Exists(probeHome));
            Assert.False(File.Exists(Path.Combine(probeHome, "auth.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_ReportsRejectedAutomaticSwitchWithoutFailingRefresh()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 1, [2] = 80 });
        var process = new FakeProcessManager
        {
            LastAccount = 1,
            StartException = new InvalidOperationException("Während einer Auto-Aufgabe gesperrt.")
        };
        var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => true);
        string? notice = null;
        monitor.Notice += (_, message) => notice = message;

        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Null(process.StartedAccount);
            Assert.Contains("nicht ausgeführt", notice);
            Assert.Contains("Auto-Aufgabe", notice);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_WaitsForDesktopCloseBeforeAutoSwitch()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 1, [2] = 80 });
        var process = new FakeProcessManager { LastAccount = 1, IsDesktopRunning = true };
        using var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9));
        try
        {
            await monitor.RefreshAsync(CancellationToken.None);
            Assert.Null(process.StartedAccount);
            Assert.Equal(2, monitor.WaitingForCloseAccount);

            process.IsDesktopRunning = false;
            await monitor.RefreshAsync(CancellationToken.None);
            Assert.Equal(2, process.StartedAccount);
            Assert.Null(monitor.WaitingForCloseAccount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_StaleLimitNeverStartsDeferredSwitch()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitor-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Encoding.UTF8.GetBytes("account-1"));
        store.Save(2, Encoding.UTF8.GetBytes("account-2"));
        var protocol = new FakeProtocolClient(new Dictionary<int, int> { [1] = 1, [2] = 80 });
        var process = new FakeProcessManager { LastAccount = 1, IsDesktopRunning = true };
        using var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9));
        try
        {
            await monitor.RefreshAsync(CancellationToken.None);
            protocol.FailingAccounts.Add(1);
            process.IsDesktopRunning = false;
            await monitor.RefreshAsync(CancellationToken.None);
            Assert.True(monitor.Current[1].IsStale);
            Assert.Null(process.StartedAccount);
            Assert.Null(monitor.WaitingForCloseAccount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_AccountChangesDuringActiveProbeDoesNotOverwriteStoredLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexMonitorIdentity-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        string activeHome = Path.Combine(root, "active");
        Directory.CreateDirectory(activeHome);
        string activeAuth = Path.Combine(activeHome, "auth.json");
        File.WriteAllBytes(activeAuth, first);
        var process = new FakeProcessManager { ActiveAccount = 1, ActiveCodexHome = activeHome };
        var protocol = new CallbackProtocolClient(_ =>
        {
            File.WriteAllBytes(activeAuth, second);
            process.ActiveAccount = 2;
            process.IsRunning = true;
            return AvailableLimits();
        });
        using var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => false);
        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(first, store.Load(1));
            Assert.Equal(second, store.Load(2));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_ProbeChangedToOtherAccountDoesNotOverwriteStoredLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexProbeIdentity-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        var process = new FakeProcessManager();
        var protocol = new CallbackProtocolClient(home =>
        {
            if (Path.GetFileName(home) == "1")
                File.WriteAllBytes(Path.Combine(home, "auth.json"), second);
            return AvailableLimits();
        });
        using var monitor = new LimitMonitor(paths, store, protocol, process, TimeSpan.FromMinutes(9), () => false);
        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(first, store.Load(1));
            Assert.Equal(second, store.Load(2));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_SameAccountTokenRefreshUpdatesStoredLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexProbeRefresh-{Guid.NewGuid():N}");
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        store.Save(1, Auth(1));
        byte[] refreshed = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tokens = new { account_id = "account-1", access_token = "refreshed" }
        });
        var protocol = new CallbackProtocolClient(home =>
        {
            File.WriteAllBytes(Path.Combine(home, "auth.json"), refreshed);
            return AvailableLimits();
        });
        using var monitor = new LimitMonitor(paths, store, protocol, new FakeProcessManager(),
            TimeSpan.FromMinutes(9), () => false);
        try
        {
            await monitor.RefreshAsync(CancellationToken.None);

            Assert.Equal(refreshed, store.Load(1));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] Auth(int account) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        tokens = new { account_id = $"account-{account}" }
    });

    private static AccountLimits AvailableLimits() => new(
        new RateLimitWindow(20, null, 300), null, DateTimeOffset.Now);

    private sealed class CallbackProtocolClient(Func<string, AccountLimits> callback) : ICodexProtocolClient
    {
        public Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken) =>
            Task.FromResult(callback(codexHome));
    }

    private sealed class FakeProtocolClient(
        IReadOnlyDictionary<int, int> primaryRemaining,
        IReadOnlyDictionary<int, int>? secondaryRemaining = null) : ICodexProtocolClient
    {
        public HashSet<int> FailingAccounts { get; } = [];
        public Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken)
        {
            int account = int.Parse(Path.GetFileName(codexHome));
            if (FailingAccounts.Contains(account))
                throw new InvalidOperationException("Probe failed");
            int primaryUsed = 100 - primaryRemaining[account];
            int secondaryUsed = 100 - (secondaryRemaining?.GetValueOrDefault(account) ?? 80);
            return Task.FromResult(new AccountLimits(
                new RateLimitWindow(primaryUsed, null, 300),
                new RateLimitWindow(secondaryUsed, null, 10080),
                DateTimeOffset.Now));
        }
    }

    private sealed class FakeProcessManager : ICodexProcessManager, ISmoothSwitchControl
    {
        public bool IsRunning { get; set; }
        public bool IsDesktopRunning { get; set; }
        public bool ProxyActive => false;
        public bool HasActiveTurn => false;
        public int? ActiveAccount { get; set; }
        public int? PendingAccount => null;
        public int? LastAccount { get; set; }
        public string ActiveCodexHome { get; set; } = string.Empty;
        public int? StartedAccount { get; private set; }
        public bool ResumedLast { get; private set; }
        public Exception? StartException { get; set; }
        public event EventHandler? ProcessExited;

        public void RaiseProcessExited() => ProcessExited?.Invoke(this, EventArgs.Empty);
        public void SynchronizeActiveAccount() { }
        public Task EmergencyStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogoutAsync(int accountNumber, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken)
        {
            if (StartException is not null)
            {
                return Task.FromException(StartException);
            }
            StartedAccount = accountNumber;
            ResumedLast = resumeLast;
            LastAccount = accountNumber;
            return Task.CompletedTask;
        }
    }
}
