namespace CodexAccountTray;

public static class LaunchMode
{
    public static bool ShouldShow(string[] args) =>
        !args.Contains("--background", StringComparer.OrdinalIgnoreCase);
}
