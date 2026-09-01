using System.Text;
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

    private sealed class FakeProtocolClient(
        IReadOnlyDictionary<int, int> primaryRemaining,
        IReadOnlyDictionary<int, int>? secondaryRemaining = null) : ICodexProtocolClient
    {
        public Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken)
        {
            int account = int.Parse(Path.GetFileName(codexHome));
            int primaryUsed = 100 - primaryRemaining[account];
            int secondaryUsed = 100 - (secondaryRemaining?.GetValueOrDefault(account) ?? 80);
            return Task.FromResult(new AccountLimits(
                new RateLimitWindow(primaryUsed, null, 300),
                new RateLimitWindow(secondaryUsed, null, 10080),
                DateTimeOffset.Now));
        }
    }

    private sealed class FakeProcessManager : ICodexProcessManager
    {
        public bool IsRunning { get; set; }
        public int? ActiveAccount { get; set; }
        public int? PendingAccount => null;
        public int? LastAccount { get; set; }
        public string ActiveCodexHome => string.Empty;
        public int? StartedAccount { get; private set; }
        public bool ResumedLast { get; private set; }
        public event EventHandler? ProcessExited;

        public void RaiseProcessExited() => ProcessExited?.Invoke(this, EventArgs.Empty);

        public Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken)
        {
            StartedAccount = accountNumber;
            ResumedLast = resumeLast;
            LastAccount = accountNumber;
            return Task.CompletedTask;
        }
    }
}
