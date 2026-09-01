using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class CodexProtocolClientTests
{
    [Fact]
    public async Task ReadLimitsAsync_ParsesPrimaryAndSecondaryWindows()
    {
        string fakeAssembly = typeof(FakeCodex.Marker).Assembly.Location;
        var command = new CodexCommand("dotnet", [fakeAssembly]);
        var client = new CodexProtocolClient(command, TimeSpan.FromSeconds(5));
        string home = Path.Combine(Path.GetTempPath(), $"CodexProtocol-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);

        try
        {
            AccountLimits result = await client.ReadLimitsAsync(home, CancellationToken.None);

            Assert.Equal(1, result.Primary?.RemainingPercent);
            Assert.Equal(60, result.Secondary?.RemainingPercent);
            Assert.Equal(300, result.Primary?.WindowDurationMins);
            Assert.Equal(10080, result.Secondary?.WindowDurationMins);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
