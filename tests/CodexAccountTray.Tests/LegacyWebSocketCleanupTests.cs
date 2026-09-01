using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class LegacyWebSocketCleanupTests
{
    [Fact]
    public void Remove_ClearsLegacyValueFromCurrentProcess()
    {
        const string name = "CODEX_APP_SERVER_WS_URL";
        string? original = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable(name, "ws://127.0.0.1:47831");

            LegacyWebSocketCleanup.Remove();

            Assert.Null(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
