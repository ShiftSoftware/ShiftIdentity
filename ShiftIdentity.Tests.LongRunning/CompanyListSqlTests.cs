using ShiftIdentity.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>The dashboard's company list on SQL Server, with the same check the SQLite suite runs.</summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class CompanyListSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>
{
    [Fact]
    public async Task The_company_list_names_each_brand_once()
    {
        await fixture.ResetAsync();
        await CompanyListChecks.EachBrandIsListedOnceAsync(fixture, TestContext.Current.CancellationToken);
    }
}
