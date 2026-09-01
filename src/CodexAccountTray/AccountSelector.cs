namespace CodexAccountTray;

public static class AccountSelector
{
    public static int? NextAvailable(int current, IEnumerable<AccountAvailability> accounts)
    {
        AccountAvailability[] ordered = accounts
            .Where(account => account.AccountNumber != current)
            .OrderBy(account => account.AccountNumber <= current ? 1 : 0)
            .ThenBy(account => account.AccountNumber)
            .ToArray();
        foreach (AccountAvailability account in ordered)
        {
            if (account.IsLoggedIn &&
                account.RemainingPercent > 1)
            {
                return account.AccountNumber;
            }
        }

        return null;
    }
}
