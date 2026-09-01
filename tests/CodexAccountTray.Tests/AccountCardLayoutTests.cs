using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AccountCardLayoutTests
{
    [Fact]
    public void Calculate_KeepsEveryButtonInsideCard()
    {
        AccountCardPositions positions = AccountCardLayout.Calculate(930);

        Assert.Equal(914, positions.StartX + 96);
        Assert.True(positions.LoginX > positions.LimitX);
        Assert.True(positions.LimitWidth > 100);
    }
}
