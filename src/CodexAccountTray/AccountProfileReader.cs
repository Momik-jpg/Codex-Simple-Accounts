using System.Text;
using System.Text.Json;

namespace CodexAccountTray;

public static class AccountProfileReader
{
    public static string? ReadName(byte[] auth)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(auth);
            string? token = document.RootElement
                .GetProperty("tokens")
                .GetProperty("id_token")
                .GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string[] parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }
            byte[] payload = DecodeBase64Url(parts[1]);
            try
            {
                using JsonDocument claims = JsonDocument.Parse(payload);
                JsonElement root = claims.RootElement;
                if (root.TryGetProperty("https://api.openai.com/profile", out JsonElement profile) &&
                    profile.TryGetProperty("name", out JsonElement profileName))
                {
                    return Clean(profileName.GetString());
                }
                return root.TryGetProperty("name", out JsonElement name)
                    ? Clean(name.GetString())
                    : null;
            }
            finally
            {
                Array.Clear(payload);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static string? ReadAccountId(byte[] auth)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(auth);
            return document.RootElement
                .GetProperty("tokens")
                .GetProperty("account_id")
                .GetString();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
