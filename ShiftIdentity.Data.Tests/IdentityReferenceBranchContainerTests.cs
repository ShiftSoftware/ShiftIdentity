using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;
using ShiftIdentity.Data.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// Where the reference source reads branches from: the Branches container, one document per branch, and never the
/// container that also holds the branch link documents.
/// </summary>
public class IdentityReferenceBranchContainerTests
{
    [Fact]
    public async Task Every_branch_read_goes_to_the_Branches_container()
    {
        var source = new StubIdentityReferenceSource();
        source.Seed(Branch("1"));

        await source.ResolveCompanyBranchAsync("1");
        await source.GetCompanyBranchesAsync();
        using (await source.LoadCompanyBranchesSnapshotAsync()) { }
        await source.RefreshAsync(IdentityReferenceFamily.CompanyBranches);
        await source.RefreshAsync(IdentityReferenceFamily.CompanyBranches, "1");

        Assert.Equal(
            Enumerable.Repeat(("Branches", (string?)CompanyBranchContainerItemTypes.Branch), 5),
            source.Reads);
    }

    [Fact]
    public async Task A_host_that_renames_the_container_is_read_from_its_own()
    {
        var source = new StubIdentityReferenceSource(options: new CosmosIdentityReferenceOptions { BranchContainerName = "BranchDocs" });
        source.Seed(Branch("1"));

        await source.GetCompanyBranchesAsync();

        Assert.Equal(("BranchDocs", (string?)CompanyBranchContainerItemTypes.Branch), Assert.Single(source.Reads));
    }

    private static CompanyBranchModel Branch(string id)
        => new()
        {
            id = id,
            BranchID = id,
            ItemType = CompanyBranchContainerItemTypes.Branch,
            Name = "Main",
        };
}
