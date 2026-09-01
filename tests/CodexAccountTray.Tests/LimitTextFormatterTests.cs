using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class LimitTextFormatterTests
{
    [Fact]
    public void Format_ShowsRemainingPercentAndResetTime()
    {
        long reset = new DateTimeOffset(2030, 1, 2, 16, 20, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var window = new RateLimitWindow(27, reset, 300);

        string text = LimitTextFormatter.Format(window, "5 h", TimeZoneInfo.Utc);

        Assert.Equal("5 h: 73 % frei · neu 02.01. 16:20", text);
    }

    [Fact]
    public void FormatWithoutData_ShowsUnavailable()
    {
        Assert.Equal("Woche: nicht verfügbar", LimitTextFormatter.Format(null, "Woche", TimeZoneInfo.Utc));
    }
}
