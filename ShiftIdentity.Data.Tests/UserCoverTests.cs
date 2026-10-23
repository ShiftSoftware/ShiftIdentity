using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// Review F16's cover: an admin covers a user when they cover each of the user's trees, as each one's assignment is
/// checked. One tree can't hold what several grant together, so the trees aren't reduced as one: reduced as one, TCA's
/// "System: Basic" and "Data by Branch: Self" made 194 of 277 users on the 23 October backup unchangeable by any admin,
/// full access included.
/// </summary>
public class UserCoverTests
{
    // A wildcard on two dynamic actions, as "System: Basic" grants them.
    private const string SystemBasic = """{"ShiftIdentityActions":{"DataLevelAccess":{"Regions":["w","r"],"Cities":["w","r"]},"Users":["r"]}}""";

    // The self row of the same actions, beyond the wildcard, as "Data by Branch: Self" grants it.
    private const string DataByBranchSelf = """{"ShiftIdentityActions":{"DataLevelAccess":{"Regions":{"_shift_software_type_auth_core_self_reference":["r","w","d"]},"Cities":{"_shift_software_type_auth_core_self_reference":["r","w","d"]}}}}""";

    private static TypeAuthContext Admin(string tree) => new(tree, typeof(ShiftIdentityActions));

    [Fact]
    public void A_full_access_admin_covers_a_user_whose_trees_overlap()
    {
        var admin = Admin(FullAccessTree.BuildJson([typeof(ShiftIdentityActions)]));

        Assert.Empty(UserCover.Uncovered(admin, [SystemBasic, DataByBranchSelf]));
    }

    [Fact]
    public void What_the_admin_lacks_in_one_of_the_trees_is_still_reported()
    {
        // Every row to read and write, none to delete.
        var admin = Admin("""{"ShiftIdentityActions":{"DataLevelAccess":["r","w"],"Users":["r"]}}""");

        var uncovered = UserCover.Uncovered(admin, [SystemBasic, DataByBranchSelf]);

        Assert.Equal(["Cities=Self: Delete", "Regions=Self: Delete"], uncovered.Select(x => $"{x.Key.Name}={x.Value}").Order());
    }
}
