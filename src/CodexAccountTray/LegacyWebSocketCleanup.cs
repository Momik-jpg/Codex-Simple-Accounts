using System.Runtime.InteropServices;

namespace CodexAccountTray;

public static class LegacyWebSocketCleanup
{
    private const string Name = "CODEX_APP_SERVER_WS_URL";
    private const string LegacyValue = "ws://127.0.0.1:47831";

    public static void Remove()
    {
        Environment.SetEnvironmentVariable(Name, null, EnvironmentVariableTarget.Process);
        string? current = Environment.GetEnvironmentVariable(
            Name,
            EnvironmentVariableTarget.User);
        if (!string.Equals(current, LegacyValue, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Environment.SetEnvironmentVariable(Name, null, EnvironmentVariableTarget.User);
        SendMessageTimeout(
            0xffff,
            0x001A,
            0,
            "Environment",
            0x0002,
            3000,
            out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nint result);
}
