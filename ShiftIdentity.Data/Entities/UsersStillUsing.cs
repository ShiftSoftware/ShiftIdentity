using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Finds the users who still use a row that is about to be deleted, so the delete can be refused while any remain.
// Deleted users do not count.
internal static class UsersStillUsing
{
    // How many usernames a refusal names. It also gives the total.
    private const int NamesShown = 5;

    // Null when no user who is not deleted uses the row. Otherwise up to five usernames in username order, and the total.
    internal static async Task<(string Names, int Total)?> FindAsync(IQueryable<User> users)
    {
        var remaining = users.Where(user => !user.IsDeleted);
        var total = await remaining.Select(user => user.ID).Distinct().CountAsync();

        if (total == 0)
            return null;

        var names = await remaining.Select(user => user.Username).Distinct().OrderBy(name => name).Take(NamesShown).ToListAsync();

        return (string.Join(", ", names), total);
    }
}
