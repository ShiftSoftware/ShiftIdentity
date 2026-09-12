using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

internal sealed class IdentitySessionTestHost : IDisposable
{
    private readonly ServiceProvider services;
    public IdentitySession Session => services.GetRequiredService<IdentitySession>();
    public IIdentityTokenStorage Storage => services.GetRequiredService<IIdentityTokenStorage>();
    public AuthenticationStateProvider Auth => services.GetRequiredService<AuthenticationStateProvider>();
    public BrowserStorageRuntime Browser { get; } = new();

    public IdentitySessionTestHost(bool admission, HttpMessageHandler handler, IIdentityTokenStorage? storage = null,
        bool browser = false, bool cookie = false)
    {
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IJSRuntime>(Browser);
        if (!browser) registrations.AddSingleton<IIdentityTokenStorage>(storage ?? new InMemoryIdentityTokenStorage());
        registrations.AddShiftIdentity("synthetic-app", "https://identity.invalid/", "https://client.invalid/", configure: options =>
        {
            options.RefreshTokenStorage = cookie ? RefreshTokenStorage.Cookie : RefreshTokenStorage.LocalStorage;
            options.CookieDomain = ".example.invalid";
        });
        registrations.AddScoped(_ => new ShiftIdentityHttpClient(handler) { BaseAddress = new("https://identity.invalid/") });
        if (admission) registrations.AddIdentityAdmissionSession(_ => new HttpClient(handler) { BaseAddress = new("https://identity.invalid/") });
        services = registrations.BuildServiceProvider();
    }

    public void Dispose() => services.Dispose();
}

internal sealed class RenewalHttp(bool admission, Func<Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int calls;
    public int Calls => calls;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(admission ? "/api/identity/v2/refresh" : "/auth/Refresh", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Null(request.Headers.Authorization);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refreshToken").GetString()));
        Interlocked.Increment(ref calls);
        Started.TrySetResult();
        return await respond();
    }

    public static HttpResponseMessage Success(bool admission, TokenDTO token) => new(HttpStatusCode.OK)
    {
        Content = admission ? JsonContent.Create<AuthOutcome>(new SessionIssued(token)) :
            JsonContent.Create(new ShiftEntityResponse<TokenDTO> { Entity = token })
    };
    public static HttpResponseMessage Refused(bool admission, AuthenticationFailure failure = AuthenticationFailure.InvalidGrant) => new(HttpStatusCode.Unauthorized)
    {
        Content = admission ? JsonContent.Create<AuthOutcome>(new AuthenticationRefused(failure)) : JsonContent.Create(new { })
    };
}

// Exercises the framework's real Blazored storage and cookie service without a browser or network.
internal sealed class BrowserStorageRuntime : IJSInProcessRuntime
{
    public Dictionary<string, string> Local { get; } = [];
    public string? RefreshCookie { get; set; }
    public List<string> CookieWrites { get; } = [];
    public TValue Invoke<TValue>(string identifier, params object?[]? args)
    {
        object? result = null;
        var key = args?.FirstOrDefault()?.ToString() ?? "";
        switch (identifier)
        {
            case "localStorage.getItem": result = Local.GetValueOrDefault(key); break;
            case "localStorage.setItem": Local[key] = args![1]!.ToString()!; break;
            case "localStorage.removeItem": Local.Remove(key); break;
            case "eval" when key == "decodeURIComponent(document.cookie)":
                result = RefreshCookie is null ? "" : "refresh-token=" + RefreshCookie; break;
            case "eval" when key.TrimStart().StartsWith("document.cookie"):
                CookieWrites.Add(key);
                var start = key.IndexOf("refresh-token=", StringComparison.Ordinal) + "refresh-token=".Length;
                RefreshCookie = key[start..key.IndexOf(';', start)];
                if (key.Contains("max-age=0;")) RefreshCookie = null;
                break;
            default: throw new InvalidOperationException("Unexpected browser call: " + identifier);
        }
        return result is null ? default! : (TValue)result;
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => new(Invoke<TValue>(identifier, args));
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => new(Invoke<TValue>(identifier, args));
    public void Dispose() { }
}
