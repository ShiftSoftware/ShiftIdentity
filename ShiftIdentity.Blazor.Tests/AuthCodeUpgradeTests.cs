using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Handlers;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

public sealed class AuthCodeUpgradeTests
{
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("2", true, 0)]
    [InlineData("1", false, 0)]
    public async Task Old_unexpired_dashboard_session_exchanges_before_the_app_code_request(string schema, bool staged, int exchanges)
    {
        var renewed = IdentitySessionTests.Token("renewed");
        using var refresh = new RenewalHttp(false, () => Task.FromResult(RenewalHttp.Success(false, renewed)));
        using var host = new IdentitySessionTestHost(false, refresh);
        var original = IdentitySessionTests.Token("original", schema: schema);
        await host.Session.StoreTokenAsync(original);
        using var outbound = new AppCodeHttp(() => Assert.Equal(exchanges == 1 ? renewed.Token : original.Token, host.Session.GetToken()));
        using var client = new HttpClient(outbound) { BaseAddress = new("https://identity.invalid/api/") };
        var service = new AuthService(new HttpService(client), host.Session, host.Auth, new TestNavigation(), client,
            new ShiftIdentityDashboardBlazorOptions { StagedAuthority = staged });
        await service.GenerateAuthCodeAsync(new GenerateAuthCodeDTO { AppId = "old-consumer", CodeChallenge = "challenge" });
        Assert.Equal(exchanges, refresh.Calls);
    }

    private sealed class AppCodeHttp(Action check) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            check();
            Assert.Equal("/api/auth/AuthCode", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel>(new()))
            });
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://identity.invalid/", "https://identity.invalid/Auth/AuthCode");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation");
    }
}
