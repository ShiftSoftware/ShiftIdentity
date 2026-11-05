using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// An app sends the browser to the sign-in page while the session saved there has already ended. The API answers the
/// app code request with 401 and no body. The page must not crash on that answer: it forgets the saved session and
/// loads the page again, and the visitor then reaches the login screen.
/// </summary>
[Trait("Category", "Ui")]
public sealed class AuthCodeEndedSessionTests
{
    private const string Address = "/Identity/Auth/AuthCode?AppId=consumer&CodeChallenge=challenge&ReturnUrl=orders";

    [Fact]
    public async Task A_refused_session_is_forgotten_and_the_same_page_is_loaded_again()
    {
        // A browser leaves the page on a forced load. bUnit stays, and sets the query parameters again, so the page asks
        // again. Only the first answer is the refusal, so that this does not repeat without end.
        var asked = 0;
        await using var context = Context(() => ++asked == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.BadRequest), out var host);
        await host.Session.StoreTokenAsync(IdentitySessionTests.Token("ended"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(Address);

        var cut = context.Render<AuthCode>();

        cut.WaitForAssertion(() => Assert.True(navigation.History.First().Options.ForceLoad));
        Assert.Equal("http://localhost" + Address, navigation.Uri);
        // The next load finds no session, so it goes to the login screen and cannot come back here in a loop.
        Assert.Null(host.Session.GetToken());
    }

    [Fact]
    public async Task A_refusal_with_a_message_keeps_the_session_and_does_not_reload()
    {
        await using var context = Context(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel> { Message = new Message { Body = "Refused" } })
        }, out var host);
        await host.Session.StoreTokenAsync(IdentitySessionTests.Token("current"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(Address);
        var before = navigation.History.Count;

        context.Render<AuthCode>();

        Assert.Equal(before, navigation.History.Count);
        Assert.NotNull(host.Session.GetToken());
    }

    [Fact]
    public async Task A_page_that_skips_the_consent_step_does_not_crash_on_a_refusal()
    {
        await using var context = Context(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel> { Message = new Message { Body = "Refused" } })
        }, out var host);
        await host.Session.StoreTokenAsync(IdentitySessionTests.Token("current"));
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(Address + "&NoNeedAuthCode=true");
        var before = navigation.History.Count;

        context.Render<AuthCode>();

        Assert.Equal(before, navigation.History.Count);
    }

    public static TheoryData<Func<HttpResponseMessage>, HttpStatusCode, string?> Answers => new()
    {
        { () => new HttpResponseMessage(HttpStatusCode.Unauthorized), HttpStatusCode.Unauthorized, null },
        { () => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "text/html") }, HttpStatusCode.BadGateway, null },
        { () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel> { Message = new Message { Body = "Refused" } }) }, HttpStatusCode.BadRequest, "Refused" },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task Every_write_keeps_a_failed_answer_s_status_and_reads_its_message_only_when_it_has_one(
        Func<HttpResponseMessage> answer, HttpStatusCode status, string? message)
    {
        using var http = new HttpClient(new Answering(answer)) { BaseAddress = new("https://identity.invalid/api/") };
        var service = new HttpService(http);
        var dto = new GenerateAuthCodeDTO { AppId = "consumer", CodeChallenge = "challenge" };

        var post = await service.PostAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>("write", dto);
        var put = await service.PutAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>("write", dto);
        var delete = await service.DeleteAsync<ShiftEntityResponse<AuthCodeModel>>("write");

        foreach (var result in new[] { post, put, delete })
        {
            Assert.Equal(status, result.StatusCode);
            Assert.False(result.IsSuccess);
            Assert.Equal(message, result.Data?.Message?.Body);
        }
    }

    [Fact]
    public async Task A_successful_write_still_reads_its_answer()
    {
        using var http = new HttpClient(new Answering(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ShiftEntityResponse<AuthCodeModel>(new() { AppId = "consumer" }))
        })) { BaseAddress = new("https://identity.invalid/api/") };

        var result = await new HttpService(http).PostAsync<ShiftEntityResponse<AuthCodeModel>, GenerateAuthCodeDTO>("write", new());

        Assert.True(result.IsSuccess);
        Assert.Equal("consumer", result.Data!.Entity!.AppId);
    }

    private static BunitContext Context(Func<HttpResponseMessage> answer, out IdentitySessionTestHost host)
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var session = new IdentitySessionTestHost(false, new Answering(answer));
        host = session;
        context.Services.AddSingleton(session);
        var client = new HttpClient(new Answering(answer)) { BaseAddress = new("https://identity.invalid/api/") };
        context.Services.AddSingleton(sp => new AuthService(new HttpService(client), session.Session, session.Auth,
            sp.GetRequiredService<NavigationManager>(), client));
        return context;
    }

    private sealed class Answering(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer());
    }
}
