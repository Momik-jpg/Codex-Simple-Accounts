using System.Text.Json;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class SmoothCodexProcessManagerTests
{
    [Fact]
    public void Constructor_RecognizesActiveStoredAccount()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothIdentity-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "tray"));
        var store = new AccountStore(paths);
        byte[] auth = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tokens = new { account_id = "account-two", id_token = "a.e30." }
        });
        store.Save(2, auth);
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        File.WriteAllBytes(Path.Combine(codexHome, "auth.json"), auth);

        using var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome,
            new FakeDesktopRuntime());

        Assert.Equal(2, manager.ActiveAccount);
        Assert.Equal(2, manager.LastAccount);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task StartAsync_ClosesChatGptBeforeSwitchingAccount()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothManager-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "tray"));
        var store = new AccountStore(paths);
        store.Save(1, Auth(1));
        store.Save(2, Auth(2));
        var desktop = new FakeDesktopRuntime();
        string fakeAssembly = typeof(FakeCodex.Marker).Assembly.Location;
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [fakeAssembly]),
            Path.Combine(root, ".codex"),
            desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);
            await manager.StartAsync(2, false, CancellationToken.None);

            Assert.Equal(1, desktop.CloseCount);
            Assert.Equal(2, manager.ActiveAccount);
            Assert.Equal(2, desktop.LaunchCount);
            Assert.False(manager.ProxyActive);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EmergencyStopAsync_KeepsStoredAccounts()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothEmergency-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "tray"));
        var store = new AccountStore(paths);
        store.Save(1, Auth(1));
        store.Save(2, Auth(2));
        var desktop = new FakeDesktopRuntime();
        string fakeAssembly = typeof(FakeCodex.Marker).Assembly.Location;
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [fakeAssembly]),
            Path.Combine(root, ".codex"),
            desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);
            await manager.EmergencyStopAsync(CancellationToken.None);

            Assert.False(manager.ProxyActive);
            Assert.Null(manager.ActiveAccount);
            Assert.True(store.IsLoggedIn(1));
            Assert.True(store.IsLoggedIn(2));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LogoutAsync_RemovesActiveCredentialAndStopsManagedBackend()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothLogout-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "tray"));
        var store = new AccountStore(paths);
        store.Save(1, Auth(1));
        var desktop = new FakeDesktopRuntime();
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            Path.Combine(root, ".codex"),
            desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);

            await manager.LogoutAsync(1, CancellationToken.None);

            Assert.False(store.IsLoggedIn(1));
            Assert.False(manager.ProxyActive);
            Assert.Null(manager.ActiveAccount);
            Assert.False(File.Exists(Path.Combine(manager.ActiveCodexHome, "auth.json")));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_RestartsUnmanagedChatGptAutomatically()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothRestart-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "tray"));
        var store = new AccountStore(paths);
        store.Save(1, Auth(1));
        var desktop = new FakeDesktopRuntime { IsRunning = true };
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            Path.Combine(root, ".codex"),
            desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);

            Assert.Equal(1, desktop.CloseCount);
            Assert.Equal(1, manager.ActiveAccount);
            Assert.Null(desktop.ProxyUri);
            Assert.True(desktop.IsRunning);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_DoesNotRouteChatGptThroughCustomProxy()
    {
        string root = Path.Combine(Path.GetTempPath(), $"NormalDesktop-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        store.Save(1, Auth(1));
        var desktop = new FakeDesktopRuntime();
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            Path.Combine(root, ".codex"),
            desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);

            Assert.Null(desktop.ProxyUri);
            Assert.False(manager.ProxyActive);
            Assert.Equal(1, manager.ActiveAccount);
            Assert.True(desktop.IsRunning);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static byte[] Auth(int account) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        tokens = new { account_id = $"account-{account}", id_token = "a.e30." }
    });

    private sealed class FakeDesktopRuntime : IProxyDesktopRuntime
    {
        public bool IsRunning { get; set; }
        public int CloseCount { get; private set; }
        public int LaunchCount { get; private set; }
        public Uri? ProxyUri { get; private set; }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }
        public void Launch()
        {
            LaunchCount++;
            IsRunning = true;
        }
    }
}
