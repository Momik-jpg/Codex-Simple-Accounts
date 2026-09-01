namespace CodexAccountTray;

public sealed record RateLimitWindow(int UsedPercent, long? ResetsAt, long? WindowDurationMins)
{
    public int RemainingPercent => RemainingFromUsed(UsedPercent);

    public bool RequiresSwitch => RemainingPercent <= 1;

    public static int RemainingFromUsed(int used) => Math.Clamp(100 - used, 0, 100);
}

public sealed record AccountLimits(
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary,
    DateTimeOffset CheckedAt,
    bool IsStale = false)
{
    public bool RequiresSwitch =>
        Primary is not null && Primary.RemainingPercent <= 1 ||
        Secondary is not null && Secondary.RemainingPercent == 0;

    public int AvailablePrimaryPercent =>
        RequiresSwitch ? 0 : Primary?.RemainingPercent ?? 0;
}

public sealed record AccountAvailability(int AccountNumber, bool IsLoggedIn, int RemainingPercent);
