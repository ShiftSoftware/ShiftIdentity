using System.Globalization;
using ShiftSoftware.ShiftEntity.Core.Notifications;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Data.Authorization;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;

namespace ShiftSoftware.ShiftIdentity.Data.Notifications;

/// <summary>
/// Answers the notification send path from the replicated Identity documents: the same subject resolver as the rest of
/// reverse access, and team and branch documents read through the same source. It is scoped, and each event has its own
/// scope, so one event is one resolver scope.
/// </summary>
internal sealed class IdentityNotificationDirectory : INotificationIdentityDirectory
{
    private readonly IdentitySubjectResolver resolver;
    private readonly IIdentityReferenceSource references;

    public IdentityNotificationDirectory(IdentitySubjectResolver resolver, IIdentityReferenceSource references)
    {
        this.resolver = resolver;
        this.references = references;
    }

    public async Task<NotificationUserLookup> LookUpUsersAsync(IReadOnlyCollection<long> userIDs, CancellationToken cancellationToken = default)
    {
        var resolution = await resolver.ResolveAsync(userIDs, cancellationToken);
        var eligible = new List<NotificationUser>(resolution.Eligible.Count);

        foreach (var subject in resolution.Eligible)
        {
            // The subject carries the claims. The verified flag is only on the user's document.
            var document = await references.ResolveUserAsync(subject.UserId, cancellationToken);

            eligible.Add(new(subject.UserId, subject.TypeAuth, subject, document?.Email, document?.EmailVerified == true));
        }

        return new(
            eligible,
            resolution.NotEligible.ToDictionary(item => item.Key, item => item.Value.ToString()),
            resolution.Unresolved.ToDictionary(item => item.Key, item => item.Value.ToString()));
    }

    public async Task<IReadOnlyList<long>?> GetTeamMembersAsync(long teamID, CancellationToken cancellationToken = default)
    {
        var teams = await references.GetTeamsAsync(IdentityLifecycleFilter.All, cancellationToken);

        return teams.TryGetValue(teamID.ToString(CultureInfo.InvariantCulture), out var team) ? team.UserIDs : null;
    }

    public async Task<IReadOnlyList<TaggedTextDTO>?> GetBranchEmailsAsync(long branchID, CancellationToken cancellationToken = default)
    {
        var branch = await references.ResolveCompanyBranchAsync(branchID, cancellationToken);

        return branch?.Emails;
    }
}
