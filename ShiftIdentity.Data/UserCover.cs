using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// An administrator changes another user's username, password, sign-in, email, phone or status only when their rights
/// cover everything that user holds (review F16): otherwise they could sign in as the user and hold more than they
/// were given, or lock out a user who holds more than them. What the user holds is their own tree and every assigned tree, deleted trees included, as sign-in loads
/// them. Cover is the assignment rule: <see cref="AccessTreeComparer.FindInAccessibleActionsOn"/> finds nothing.
/// </summary>
public static class UserCover
{
    /// <summary>
    /// The trees a loaded user holds: their own tree, then every assigned tree, deleted trees included. Both sign-in
    /// issuers put exactly these in the token, and the cover check reads the same, so the two can't drift apart. The
    /// user must be loaded with <c>AccessTrees</c> and each one's <c>AccessTree</c>.
    /// </summary>
    public static List<string> HeldTrees(Entities.User user)
    {
        var trees = new List<string>();

        if (!string.IsNullOrWhiteSpace(user.AccessTree))
            trees.Add(user.AccessTree);

        foreach (var assigned in user.AccessTrees ?? [])
            trees.Add(assigned.AccessTree.Tree);

        return trees;
    }

    /// <summary>The trees each user holds (<see cref="HeldTrees"/>), read from the database as it is now.</summary>
    public static async Task<Dictionary<long, List<string>>> HeldTreesAsync(ShiftIdentityDbContext db, IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .Where(x => userIds.Contains(x.ID)).ToListAsync(cancellationToken);

        return users.ToDictionary(x => x.ID, HeldTrees);
    }

    /// <summary>
    /// What the trees grant that <paramref name="admin"/> doesn't hold, per action. This is the assignment check too: a
    /// tree an admin adds or removes is checked as a user holding only that tree.
    /// </summary>
    public static Dictionary<ActionBase, string> Uncovered(TypeAuthContext admin, IEnumerable<string?> heldTrees)
    {
        var trees = heldTrees.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

        if (trees.Count == 0)
            return new Dictionary<ActionBase, string>();

        return admin.FindInAccessibleActionsOn(new TypeAuthContext(trees, admin.GetRegisteredActionTrees()));
    }

    /// <summary>
    /// The users <paramref name="admin"/> doesn't cover, with what each holds beyond the admin, from the database.
    /// Protected users are left out: they are refused everywhere already.
    /// </summary>
    public static async Task<List<(string Username, Dictionary<ActionBase, string> Uncovered)>> FindUncoveredAsync(
        ShiftIdentityDbContext db, TypeAuthContext admin, IEnumerable<Entities.User> users, CancellationToken cancellationToken = default)
    {
        var targets = users.Where(x => !x.IsProtected).ToList();
        var held = await HeldTreesAsync(db, targets.Select(x => x.ID).ToList(), cancellationToken);

        return targets
            .Select(x => (x.Username, Uncovered: Uncovered(admin, held.GetValueOrDefault(x.ID) ?? [])))
            .Where(x => x.Uncovered.Count > 0)
            .ToList();
    }

    /// <summary>
    /// The refusal, shaped as the assignment check's: under the body each user, and under each user the actions with
    /// what the admin doesn't hold. <paramref name="text"/> localizes the title and the body.
    /// </summary>
    public static Message Refusal(Func<string, string> text, IEnumerable<(string Username, Dictionary<ActionBase, string> Uncovered)> users, bool bulk) =>
        new(text("Error"), text(bulk ? AdministratorCover.BulkMessage : AdministratorCover.Message),
            users.Select(x => new Message(x.Username, null!, x.Uncovered.Select(y => new Message(y.Key.Name!, y.Value)).ToList())).ToList());
}
