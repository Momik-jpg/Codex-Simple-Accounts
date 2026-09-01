namespace CodexAccountTray;

public static class LimitTextFormatter
{
    public static string Format(RateLimitWindow? window, string label, TimeZoneInfo timeZone)
    {
        if (window is null)
        {
            return $"{label}: nicht verfügbar";
        }

        string reset = string.Empty;
        if (window.ResetsAt is long unixTime)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeSeconds(unixTime),
                timeZone);
            reset = $" · neu {local:dd.MM. HH:mm}";
        }

        return $"{label}: {window.RemainingPercent} % frei{reset}";
    }
}
