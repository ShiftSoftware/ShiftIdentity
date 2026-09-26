using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// Allow on the sign-in page sends the browser to the app's RedirectUri followed by the client's /Auth/Token route.
/// A RedirectUri registered with a trailing slash must still give exactly one slash before Auth/Token
/// (FE-2026-09-25-02). The stored RedirectUri is not changed, only the built address.
/// </summary>
[Trait("Category", "Ui")]
public sealed class AuthCodeCallbackTests
{
    private static readonly Guid Code = Guid.Parse("7b0f4c38-7a64-4c52-9c43-2d7f1d6c0e11");

    [Theory]
    [InlineData("https://app.invalid", "https://app.invalid/Auth/Token")]
    [InlineData("https://app.invalid/", "https://app.invalid/Auth/Token")]
    [InlineData("http://localhost:5015/", "http://localhost:5015/Auth/Token")]
    [InlineData("https://app.invalid/hub/", "https://app.invalid/hub/Auth/Token")]
    public async Task Allow_puts_exactly_one_slash_between_the_redirect_uri_and_Auth_Token(string redirectUri, string callback)
    {
        using var context = Context(redirectUri);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameters("/Identity/Auth/AuthCode", new Dictionary<string, object?>
        {
            ["AppId"] = "consumer", ["CodeChallenge"] = new string('a', 128), ["ReturnUrl"] = "/orders"
        }));
        var cut = context.Render<AuthCode>();
        // The card actions are Allow, then Deny.
        var allow = cut.WaitForElement(".mud-card-actions button");
        await cut.InvokeAsync(() => allow.Click());

        var uri = new Uri(navigation.Uri);
        Assert.Equal(callback, uri.GetLeftPart(UriPartial.Path));
        Assert.Contains($"AuthCode={Code}", uri.Query);
    }

    private static BunitContext Context(string redirectUri)
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var host = new IdentitySessionTestHost(false, new AppCodeHttp(redirectUri));
        context.Services.AddSingleton(host);
        var client = new HttpClient(new AppCodeHttp(redirectUri)) { BaseAddress = new("https://identity.invalid/api/") };
        context.Services.AddSingleton(sp => new AuthService(new HttpService(client), host.Session, host.Auth,
            sp.GetRequiredService<NavigationManager>(), client));
        return context;
    }

    private sealed class AppCodeHttp(string redirectUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/auth/AuthCode", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel>(new()
                {
                    Code = Code, AppId = "consumer", AppDisplayName = "Consumer", RedirectUri = redirectUri, ReturnUrl = "/orders"
                }))
            });
        }
    }
}
