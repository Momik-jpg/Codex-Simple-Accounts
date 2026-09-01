using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class LaunchModeTests
{
    [Fact]
    public void ShouldShow_HidesOnlyExplicitBackgroundStart()
    {
        Assert.True(LaunchMode.ShouldShow([]));
        Assert.False(LaunchMode.ShouldShow(["--background"]));
        Assert.False(LaunchMode.ShouldShow(["--BACKGROUND"]));
    }
}
