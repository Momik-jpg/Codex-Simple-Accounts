using System.Security.Cryptography;

namespace CodexAccountTray;

public sealed class AccountStore
{
    private readonly AppPaths _paths;

    public AccountStore(AppPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public bool IsLoggedIn(int accountNumber) => File.Exists(PathFor(accountNumber));

    public IReadOnlyList<int> AccountNumbers => Directory.Exists(_paths.Accounts)
        ? Directory.EnumerateFiles(_paths.Accounts, "account-*.dat")
            .Select(path => Path.GetFileNameWithoutExtension(path)["account-".Length..])
            .Select(value => int.TryParse(value, out int number) ? number : 0)
            .Where(number => number > 0)
            .Order()
            .ToArray()
        : [];

    public int NextAccountNumber() => AccountNumbers.DefaultIfEmpty(0).Max() + 1;

    public string DisplayName(int accountNumber)
    {
        if (!IsLoggedIn(accountNumber))
        {
            return $"Konto {accountNumber}";
        }
        byte[] auth = Load(accountNumber);
        try
        {
            return AccountProfileReader.ReadName(auth) ?? $"Konto {accountNumber}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(auth);
        }
    }

    public int? FindByAuthFile(string authFile)
    {
        if (!File.Exists(authFile))
        {
            return null;
        }
        byte[] active = File.ReadAllBytes(authFile);
        try
        {
            string? activeId = AccountProfileReader.ReadAccountId(active);
            foreach (int account in AccountNumbers)
            {
                byte[] stored = Load(account);
                try
                {
                    if (activeId is not null && AccountProfileReader.ReadAccountId(stored) == activeId)
                    {
                        return account;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(stored);
                }
            }
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(active);
        }
    }

    public void Save(int accountNumber, byte[] credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        string destination = PathFor(accountNumber);
        string temporary = destination + ".tmp";
        byte[] encrypted = CredentialVault.Protect(credential);
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, destination, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public byte[] Load(int accountNumber)
    {
        byte[] encrypted = File.ReadAllBytes(PathFor(accountNumber));
        try
        {
            return CredentialVault.Unprotect(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public void ImportAuthFile(int accountNumber, string authFile)
    {
        byte[] credential = File.ReadAllBytes(authFile);
        try
        {
            Save(accountNumber, credential);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }
    }

    public void Remove(int accountNumber)
    {
        string path = PathFor(accountNumber);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(int accountNumber)
    {
        if (accountNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(accountNumber));
        }

        return _paths.AccountFile(accountNumber);
    }
}
