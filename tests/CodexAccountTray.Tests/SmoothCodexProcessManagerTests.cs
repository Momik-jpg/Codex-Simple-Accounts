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

    [Fact]
    public async Task RouterRun_BlocksAccountSwitchAndLogout()
    {
        string root = Path.Combine(Path.GetTempPath(), $"RouterGuard-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        store.Save(1, Auth(1));
        var activity = new TaskRouterActivity();
        var manager = new SmoothCodexProcessManager(
            store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            Path.Combine(root, ".codex"),
            new FakeDesktopRuntime(),
            activity);
        try
        {
            using IDisposable router = activity.BeginRouterRun();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.StartAsync(1, false, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.LogoutAsync(1, CancellationToken.None));

            Assert.True(store.IsLoggedIn(1));
            Assert.False(File.Exists(Path.Combine(manager.ActiveCodexHome, "auth.json")));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedFirstLaunchRemovesNewCredential()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothFirstFailure-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        store.Save(1, Auth(1));
        string codexHome = Path.Combine(root, ".codex");
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, new FakeDesktopRuntime { ThrowOnLaunch = true });
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.StartAsync(1, false, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(codexHome, "auth.json")));
            Assert.Null(manager.ActiveAccount);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedLaunchRestoresUntrackedCredentialExactly()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothRestore-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        store.Save(1, Auth(1));
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        byte[] original = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tokens = new { account_id = "not-in-tray" }
        });
        string authFile = Path.Combine(codexHome, "auth.json");
        File.WriteAllBytes(authFile, original);
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, new FakeDesktopRuntime { ThrowOnLaunch = true });
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.StartAsync(1, false, CancellationToken.None));
            Assert.Equal(original, File.ReadAllBytes(authFile));
            Assert.Null(manager.ActiveAccount);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_ExternalAccountChangeDoesNotOverwriteStoredCredential()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothExternalSwitch-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        string authFile = Path.Combine(codexHome, "auth.json");
        File.WriteAllBytes(authFile, first);
        var desktop = new FakeDesktopRuntime { IsRunning = true };
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, desktop);
        try
        {
            File.WriteAllBytes(authFile, second);

            await manager.StartAsync(1, false, CancellationToken.None);

            Assert.Equal(first, store.Load(1));
            Assert.Equal(second, store.Load(2));
            Assert.Equal(first, File.ReadAllBytes(authFile));
            Assert.Equal(1, desktop.CloseCount);
            Assert.Equal(1, desktop.LaunchCount);
            Assert.Equal(1, manager.ActiveAccount);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LogoutAsync_ExternalAccountChangeKeepsCurrentCodexLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothExternalLogout-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        string authFile = Path.Combine(codexHome, "auth.json");
        File.WriteAllBytes(authFile, first);
        var desktop = new FakeDesktopRuntime { IsRunning = true };
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, desktop);
        try
        {
            File.WriteAllBytes(authFile, second);

            await manager.LogoutAsync(1, CancellationToken.None);

            Assert.False(store.IsLoggedIn(1));
            Assert.True(store.IsLoggedIn(2));
            Assert.Equal(second, File.ReadAllBytes(authFile));
            Assert.Equal(0, desktop.CloseCount);
            Assert.True(desktop.IsRunning);
            Assert.Equal(2, manager.ActiveAccount);
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartAsync_AccountChangeWhileClosingDoesNotCorruptPreviousSlot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothCloseRace-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        string authFile = Path.Combine(codexHome, "auth.json");
        File.WriteAllBytes(authFile, first);
        var desktop = new FakeDesktopRuntime
        {
            IsRunning = true,
            OnClose = () => File.WriteAllBytes(authFile, second)
        };
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, desktop);
        try
        {
            await manager.StartAsync(1, false, CancellationToken.None);

            Assert.Equal(first, store.Load(1));
            Assert.Equal(second, store.Load(2));
            Assert.Equal(first, File.ReadAllBytes(authFile));
        }
        finally
        {
            manager.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LogoutAsync_AccountChangeWhileClosingDoesNotDeleteOtherLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SmoothLogoutRace-{Guid.NewGuid():N}");
        var store = new AccountStore(new AppPaths(Path.Combine(root, "tray")));
        byte[] first = Auth(1);
        byte[] second = Auth(2);
        store.Save(1, first);
        store.Save(2, second);
        string codexHome = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexHome);
        string authFile = Path.Combine(codexHome, "auth.json");
        File.WriteAllBytes(authFile, first);
        var desktop = new FakeDesktopRuntime
        {
            IsRunning = true,
            OnClose = () => File.WriteAllBytes(authFile, second)
        };
        var manager = new SmoothCodexProcessManager(store,
            new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]),
            codexHome, desktop);
        try
        {
            await manager.LogoutAsync(1, CancellationToken.None);

            Assert.False(store.IsLoggedIn(1));
            Assert.True(store.IsLoggedIn(2));
            Assert.Equal(second, File.ReadAllBytes(authFile));
            Assert.Equal(2, manager.ActiveAccount);
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
        public bool ThrowOnLaunch { get; set; }
        public Action? OnClose { get; set; }
        public int CloseCount { get; private set; }
        public int LaunchCount { get; private set; }
        public Uri? ProxyUri { get; private set; }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            IsRunning = false;
            OnClose?.Invoke();
            return Task.CompletedTask;
        }
        public void Launch()
        {
            if (ThrowOnLaunch)
                throw new InvalidOperationException("Test launch failure");
            LaunchCount++;
            IsRunning = true;
        }
    }
}
