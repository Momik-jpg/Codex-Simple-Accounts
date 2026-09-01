using System.Text;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class CodexDesktopProcessManagerTests
{
    [Fact]
    public void Constructor_DetectsCurrentlyActiveStoredAccount()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexDesktop-{Guid.NewGuid():N}");
        string codexHome = Path.Combine(root, ".codex");
        try
        {
            var paths = new AppPaths(Path.Combine(root, "tray"));
            var store = new AccountStore(paths);
            byte[] accountOne = Encoding.UTF8.GetBytes(
                "{\"tokens\":{\"account_id\":\"account-1\",\"access_token\":\"old-1\"}}");
            byte[] accountTwo = Encoding.UTF8.GetBytes(
                "{\"tokens\":{\"account_id\":\"account-2\",\"access_token\":\"old-2\"}}");
            store.Save(1, accountOne);
            store.Save(2, accountTwo);
            Directory.CreateDirectory(codexHome);
            File.WriteAllText(
                Path.Combine(codexHome, "auth.json"),
                "{\"tokens\":{\"account_id\":\"account-2\",\"access_token\":\"refreshed-2\"}}");

            var manager = new CodexDesktopProcessManager(
                store,
                codexHome,
                new RecordingDesktopRuntime());

            Assert.Equal(2, manager.ActiveAccount);
            Assert.Equal(2, manager.LastAccount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task StartAsync_WhenDesktopIsClosed_ActivatesAccountAndLaunchesDesktopApp()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexDesktop-{Guid.NewGuid():N}");
        string codexHome = Path.Combine(root, ".codex");
        try
        {
            var paths = new AppPaths(Path.Combine(root, "tray"));
            var store = new AccountStore(paths);
            byte[] auth = Encoding.UTF8.GetBytes("{\"account\":2}");
            store.Save(2, auth);
            var runtime = new RecordingDesktopRuntime();
            var manager = new CodexDesktopProcessManager(store, codexHome, runtime);

            await manager.StartAsync(2, resumeLast: false, CancellationToken.None);

            Assert.Equal(auth, await File.ReadAllBytesAsync(Path.Combine(codexHome, "auth.json")));
            Assert.Equal(["launch"], runtime.Events);
            Assert.Equal(2, manager.ActiveAccount);
            Assert.Null(manager.PendingAccount);
            Assert.Equal(codexHome, manager.ActiveCodexHome);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task StartAsync_WhenDesktopIsOpen_WaitsForManualCloseBeforeSwitching()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexDesktop-{Guid.NewGuid():N}");
        string codexHome = Path.Combine(root, ".codex");
        try
        {
            var paths = new AppPaths(Path.Combine(root, "tray"));
            var store = new AccountStore(paths);
            byte[] accountOne = Encoding.UTF8.GetBytes(
                "{\"tokens\":{\"account_id\":\"account-1\"}}");
            byte[] accountTwo = Encoding.UTF8.GetBytes(
                "{\"tokens\":{\"account_id\":\"account-2\"}}");
            store.Save(1, accountOne);
            store.Save(2, accountTwo);
            Directory.CreateDirectory(codexHome);
            await File.WriteAllBytesAsync(Path.Combine(codexHome, "auth.json"), accountOne);

            var runtime = new RecordingDesktopRuntime { IsRunning = true };
            var manager = new CodexDesktopProcessManager(store, codexHome, runtime);

            await manager.StartAsync(2, resumeLast: false, CancellationToken.None);

            Assert.Equal(accountOne, await File.ReadAllBytesAsync(Path.Combine(codexHome, "auth.json")));
            Assert.Equal(1, manager.ActiveAccount);
            Assert.Equal(2, manager.PendingAccount);
            Assert.Empty(runtime.Events);

            runtime.CompleteManualClose();
            await WaitUntilAsync(() => manager.PendingAccount is null);

            Assert.Equal(accountTwo, await File.ReadAllBytesAsync(Path.Combine(codexHome, "auth.json")));
            Assert.Equal(2, manager.ActiveAccount);
            Assert.Equal(["launch"], runtime.Events);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingDesktopRuntime : ICodexDesktopRuntime
    {
        private readonly TaskCompletionSource _manualClose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Events { get; } = [];

        public bool IsRunning { get; set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _manualClose.Task.WaitAsync(cancellationToken);

        public void Launch() => Events.Add("launch");

        public void CompleteManualClose()
        {
            IsRunning = false;
            _manualClose.TrySetResult();
        }
    }
}
