using System;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

/// <summary>
/// The refusal when an administrator changes another user's username, password, sign-in, email, phone or status without
/// holding everything that user holds (review F16). The texts are localization keys.
/// </summary>
public static class AdministratorCover
{
    /// <summary>The refusal of a change to one user.</summary>
    public const string Message = "This user holds accesses that you don't hold, so you can not change their username, password, sign-in, email, phone or status";

    /// <summary>The refusal of a change to several users at once, which changes none of them.</summary>
    public const string BulkMessage = "Below users hold accesses that you don't hold, so none of the selected users was changed";

    /// <summary>A staged route's refusal in one line: the message, then each action with what the administrator doesn't hold.</summary>
    public static string Describe(Func<string, string> text, AuthenticationRefused refused) =>
        text(Message) + ": " + string.Join("; ", (refused.Uncovered ?? []).Select(x => $"{x.Action} ({x.Access})"));
}
