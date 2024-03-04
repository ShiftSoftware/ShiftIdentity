using Microsoft.JSInterop;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class CookieService
{
    private readonly IJSRuntime js;

    public CookieService(IJSRuntime js)
    {
        this.js = js;
    }

    public async Task SetItemAsStringAsync(string name, string value, string? domain = null,
        string? path = null, int? maxAgeSeconds = null, DateTimeOffset? expires = null, bool secure = false, SameSiteMode? sameSite = null)
    {
        var cookieScript = GenerateAddCookieScript(name, value, domain, path, maxAgeSeconds, expires, secure, sameSite);

        await js.InvokeVoidAsync("eval", cookieScript);
    }

    public void SetItemAsString(string name, string value, string? domain = null,
        string? path = null, int? maxAgeSeconds = null, DateTimeOffset? expires = null, bool secure = false, SameSiteMode? sameSite = null)
    {
        var cookieScript = GenerateAddCookieScript(name, value, domain, path, maxAgeSeconds, expires, secure, sameSite);

        ((IJSInProcessRuntime)js).InvokeVoid("eval", cookieScript);
    }

    public async Task SetItemAsync<T>(string name, T value, string? domain = null,
               string? path = null, int? maxAgeSeconds = null, DateTimeOffset? expires = null, bool secure = false, SameSiteMode? sameSite = null)
        where T : class
    {
        await SetItemAsStringAsync(name, JsonSerializer.Serialize(value), domain, path, maxAgeSeconds, expires, secure, sameSite);
    }

    public void SetItem<T>(string name, T value, string? domain = null,
                      string? path = null, int? maxAgeSeconds = null, DateTimeOffset? expires = null, bool secure = false, SameSiteMode? sameSite = null)
        where T : class
    {
        SetItemAsString(name, JsonSerializer.Serialize(value), domain, path, maxAgeSeconds, expires, secure, sameSite);
    }

    public async Task RemoveItemAsync(string name, string? domain = null, string? path = null)
    {
        await SetItemAsStringAsync(name, "", domain, path, 0, DateTimeOffset.Now.AddYears(-1), false, null);
    }

    public void RemoveItem(string name, string? domain = null, string? path = null)
    {
        SetItemAsString(name, "", domain, path, 0, DateTimeOffset.Now.AddYears(-1), false, null);
    }

    private string GenerateAddCookieScript(string name, string value, string? domain, string? path, int? maxAgeSeconds, DateTimeOffset? expires, bool secure, SameSiteMode? sameSite)
    {
        var cookieText = "";
        cookieText += $"{name}={value};";

        if (maxAgeSeconds.HasValue)
            cookieText += $"max-age={maxAgeSeconds.Value};";

        if (!string.IsNullOrWhiteSpace(domain))
            cookieText += $"domain={domain};";

        if (!string.IsNullOrWhiteSpace(path))
            cookieText += $"path={path};";

        if (secure)
            cookieText += "secure;";

        if (sameSite != null)
            cookieText += $"samesite={sameSite.Value.ToString().ToLower()};";

        if (expires.HasValue)
            cookieText += $"expires={expires.Value:R};";

        return $@"
            document.cookie = '{cookieText}';
        ";
    }

    private async Task<Dictionary<string, string>?> GetAllCookiesAsync()
    {
        var cookiesText = await js.InvokeAsync<string>("eval", "decodeURIComponent(document.cookie)");

        if (string.IsNullOrWhiteSpace(cookiesText))
            return null;

        var cookies = cookiesText.Split(";").Select(c => c.Split("=")).ToDictionary(c => c[0].Trim(), c => c[1].Trim());
        return cookies;
    }

    private Dictionary<string, string>? GetAllCookies()
    {
        var cookiesText = ((IJSInProcessRuntime)js).Invoke<string>("eval", "decodeURIComponent(document.cookie)");

        if (string.IsNullOrWhiteSpace(cookiesText))
            return null;

        var cookies = cookiesText.Split(";").Select(c => c.Split("=")).ToDictionary(c => c[0].Trim(), c => c[1].Trim());
        return cookies;
    }

    public async Task<string?> GetItemAsStringAsync(string name)
    {
        var cookies = await GetAllCookiesAsync();
        if (cookies == null || !cookies.ContainsKey(name))
            return null;

        return cookies[name];
    }

    public string? GetItemAsString(string name)
    {
        var cookies = GetAllCookies();
        if (cookies == null || !cookies.ContainsKey(name))
            return null;

        return cookies[name];
    }

    public async Task<T?> GetItemAsync<T>(string name)
        where T : class
    {
        var cookie = await GetItemAsStringAsync(name);
        if (cookie == null)
            return null;

        return JsonSerializer.Deserialize<T>(cookie);
    }

    public T? GetItem<T>(string name)
        where T : class
    {
        var cookie = GetItemAsString(name);
        if (cookie == null)
            return null;

        return JsonSerializer.Deserialize<T>(cookie);
    }
}

public enum SameSiteMode
{
    Lax,
    Strict,
    None
}