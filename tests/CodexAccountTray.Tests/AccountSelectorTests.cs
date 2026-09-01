using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AccountSelectorTests
{
    [Fact]
    public void NextAvailable_SkipsExhaustedAndLoggedOutAccounts()
    {
        int? result = AccountSelector.NextAvailable(1,
        [
            new AccountAvailability(2, true, 0),
            new AccountAvailability(3, false, 100),
            new AccountAvailability(4, true, 42)
        ]);

        Assert.Equal(4, result);
    }

    [Fact]
    public void NextAvailable_WrapsAroundAccountNumbers()
    {
        int? result = AccountSelector.NextAvailable(4,
        [
            new AccountAvailability(1, true, 80),
            new AccountAvailability(2, true, 70)
        ]);

        Assert.Equal(1, result);
    }

    [Fact]
    public void NextAvailable_SupportsMoreThanFourAccounts()
    {
        int? result = AccountSelector.NextAvailable(4,
        [
            new AccountAvailability(5, true, 70),
            new AccountAvailability(6, true, 60)
        ]);

        Assert.Equal(5, result);
    }
}
