using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

internal static class JwtUtils
{
    public static DateTimeOffset? GetExpirationTime(string jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken))
            return null;

        var parts = jwtToken.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var payload = parts[1];
            payload = PadBase64(payload);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var exp = expElement.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    public static bool IsExpired(string jwtToken, int secondsBefore = 0)
    {
        var exp = GetExpirationTime(jwtToken);
        if (exp == null)
            return true;
        return exp <= DateTimeOffset.UtcNow.AddSeconds(secondsBefore);
    }

    private static string PadBase64(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return base64.Replace('-', '+').Replace('_', '/');
    }
}
