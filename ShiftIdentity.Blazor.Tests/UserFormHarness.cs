using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// Renders the production User form against a scripted HTTP transport: an operator with Users read/write, the real
/// ShiftEntityForm save path, and empty OData pages for the action tree and the autocompletes.
/// </summary>
internal static class UserFormHarness
{
    public static BunitContext Create(out UserFormTransport transport)
    {
        var context = new BunitContext();
        transport = new();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}

/// <summary>Records saves, serves one existing user by key, and answers every other request with an empty OData page.</summary>
internal sealed class UserFormTransport : HttpMessageHandler
{
    public List<(string Path, string Body)> Posts { get; } = [];

    /// <summary>Served for GET IdentityUser/{ID}; the form opens it in view mode.</summary>
    public UserDTO? Existing { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post)
        {
            Posts.Add((path, await request.Content!.ReadAsStringAsync(cancellationToken)));
            var saved = new UserDTO { ID = "42", Username = "synthetic-form", FullName = "Synthetic User", IsActive = true, AccessTree = "{}",
                CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" } };
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new ShiftEntityResponse<UserDTO>(saved)) };
        }
        if (request.Method == HttpMethod.Get && Existing is not null && path == $"/IdentityUser/{Existing.ID}")
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<UserDTO>(Existing)) };
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
    }
}
