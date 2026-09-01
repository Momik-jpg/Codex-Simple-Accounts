using System.Text;
using System.Text.Json;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AccountProfileReaderTests
{
    [Fact]
    public void ReadName_UsesProfileNameFromIdToken()
    {
        byte[] auth = CreateAuth("account-1", "Daniela Maag");

        Assert.Equal("Daniela Maag", AccountProfileReader.ReadName(auth));
    }

    [Fact]
    public void ReadName_ReturnsNullForInvalidAuth()
    {
        Assert.Null(AccountProfileReader.ReadName("not-json"u8.ToArray()));
    }

    [Fact]
    public void ReadAccountId_ReadsStoredIdentity()
    {
        byte[] auth = CreateAuth("account-42", "Test");

        Assert.Equal("account-42", AccountProfileReader.ReadAccountId(auth));
    }

    private static byte[] CreateAuth(string accountId, string name)
    {
        string header = Base64Url("""{"alg":"none"}""");
        string payload = Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, string> { ["name"] = name },
            ["sub"] = "user"
        }));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            tokens = new { account_id = accountId, id_token = $"{header}.{payload}." }
        });
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
