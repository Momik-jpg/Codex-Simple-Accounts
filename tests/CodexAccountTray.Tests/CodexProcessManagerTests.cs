using System.Text;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class CodexProcessManagerTests
{
    [Fact]
    public async Task StartAsync_UsesSharedHomeAndPersistsCredentialAfterExit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexManager-{Guid.NewGuid():N}");
        string log = Path.Combine(root, "args.txt");
        Directory.CreateDirectory(root);
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        byte[] credential = Encoding.UTF8.GetBytes("{\"account\":1}");
        store.Save(1, credential);
        string fakeAssembly = typeof(FakeCodex.Marker).Assembly.Location;
        var command = new CodexCommand("dotnet", [fakeAssembly]);
        var manager = new CodexProcessManager(paths, store, command, root,
            new Dictionary<string, string?> { ["FAKE_CODEX_LOG"] = log },
            showConsole: false);

        try
        {
            await manager.StartAsync(1, resumeLast: true, CancellationToken.None);
            await manager.WaitForExitAsync(CancellationToken.None);

            Assert.False(manager.IsRunning);
            Assert.Equal(credential, store.Load(1));
            Assert.False(File.Exists(Path.Combine(paths.SharedCodexHome, "auth.json")));
            Assert.Equal(["resume", "--last", "--all"], File.ReadAllLines(log));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
