using System.Text;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AccountLoginServiceTests
{
    [Fact]
    public async Task LoginAsync_ImportsAndProtectsCreatedAuthFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexLogin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var paths = new AppPaths(root);
        var store = new AccountStore(paths);
        var command = new CodexCommand("dotnet", [typeof(FakeCodex.Marker).Assembly.Location]);
        var service = new AccountLoginService(paths, store, command,
            new Dictionary<string, string?> { ["FAKE_LOGIN_AUTH"] = "{\"account\":2}" },
            showConsole: false);

        try
        {
            await service.LoginAsync(2, CancellationToken.None);

            Assert.True(store.IsLoggedIn(2));
            Assert.Equal("{\"account\":2}", Encoding.UTF8.GetString(store.Load(2)));
            Assert.False(Directory.Exists(paths.LoginHome(2)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
