using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// The dashboard's company list on the fixture's database, through the real host: the same request and the same answer
/// on SQL Server and on SQLite. Run from both suites.
/// </summary>
public static class CompanyListChecks
{
    private const string AdminName = "company-list-admin";

    /// <summary>A brand that two of the company's branches carry is listed once; a company without branches lists none.</summary>
    public static async Task EachBrandIsListedOnceAsync(SqlIdentityFixture fixture, CancellationToken ct)
    {
        await using (var db = fixture.CreateContext())
        {
            var template = await db.CompanyBranches.AsNoTracking().FirstAsync(ct);
            var toyota = new Brand { Name = "Toyota " + Guid.NewGuid().ToString("N")[..6] };
            var lexus = new Brand { Name = "Lexus " + Guid.NewGuid().ToString("N")[..6] };
            var company = new Company { Name = "Two-branch company" };
            var empty = new Company { Name = "Company without branches" };
            db.AddRange(toyota, lexus, company, empty);
            await db.SaveChangesAsync(ct);
            db.AddRange(
                new CompanyBranch { Name = "First", CompanyID = company.ID, RegionID = template.RegionID, CityID = template.CityID,
                    CompanyBranchBrands = [new() { BrandID = toyota.ID }, new() { BrandID = lexus.ID }] },
                new CompanyBranch { Name = "Second", CompanyID = company.ID, RegionID = template.RegionID, CityID = template.CityID,
                    CompanyBranchBrands = [new() { BrandID = toyota.ID }] });
            await db.SaveChangesAsync(ct);
            if (!await db.Users.AnyAsync(x => x.Username == AdminName, ct))
                await fixture.CreateSyntheticUserAsync(AdminName, FullAccessTree.BuildJson([typeof(ShiftIdentityActions)]));
        }

        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        var session = await IdentityHttpHost.SignInAsync(host.Client, AdminName, fixture.Password);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var response = await host.Client.GetAsync("api/IdentityCompany?$top=2&$orderby=ID desc", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        var rows = Prop(JsonDocument.Parse(body).RootElement, "Value").EnumerateArray().ToList();

        // Newest first: the company without branches, then the two-branch company.
        Assert.Equal(2, rows.Count);
        Assert.Empty(Prop(rows[0], "Brands").EnumerateArray());
        var brands = Prop(rows[1], "Brands").EnumerateArray().Select(x => Prop(x, "Value").GetString()).ToList();
        Assert.Equal(2, brands.Count);
        Assert.Equal(2, brands.Distinct().Count());
    }

    private static JsonElement Prop(JsonElement element, string name) =>
        element.EnumerateObject().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
