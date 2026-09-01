using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class RateLimitTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(99, 1)]
    [InlineData(100, 0)]
    [InlineData(130, 0)]
    public void RemainingPercent_IsClamped(int used, int expected)
    {
        Assert.Equal(expected, RateLimitWindow.RemainingFromUsed(used));
    }

    [Fact]
    public void SwitchThreshold_StartsAtOnePercent()
    {
        Assert.True(new RateLimitWindow(99, null, 300).RequiresSwitch);
        Assert.False(new RateLimitWindow(98, null, 300).RequiresSwitch);
    }

    [Fact]
    public void AccountLimits_RequiresSwitchWhenWeeklyLimitIsEmpty()
    {
        var limits = new AccountLimits(
            new RateLimitWindow(20, null, 300),
            new RateLimitWindow(100, null, 10080),
            DateTimeOffset.Now);

        Assert.True(limits.RequiresSwitch);
    }

    [Fact]
    public void AccountLimits_DoesNotSwitchAtOnePercentWeeklyRemaining()
    {
        var limits = new AccountLimits(
            new RateLimitWindow(20, null, 300),
            new RateLimitWindow(99, null, 10080),
            DateTimeOffset.Now);

        Assert.False(limits.RequiresSwitch);
    }
}
