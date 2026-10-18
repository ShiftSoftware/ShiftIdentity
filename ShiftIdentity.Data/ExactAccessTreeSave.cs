using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// An access tree is saved exactly as submitted, or not at all: an admin changes only what is within their reach, in
/// either direction, so whatever they remove they can give back.
/// </summary>
internal static class ExactAccessTreeSave
{
    /// <summary>
    /// The tree to store when <paramref name="saver"/> saves <paramref name="submitted"/> over <paramref name="stored"/>
    /// (null for a new tree). Throws when it would differ from what was submitted: the saver can't grant part of it, or it
    /// removes stored grants they couldn't give back.
    /// </summary>
    internal static string Generate(TypeAuthContext submitted, ITypeAuthService saver, TypeAuthContext? stored, ShiftIdentityLocalizer loc)
    {
        var report = submitted.GenerateAccessTreeWithReport((saver as TypeAuthContext)!, stored);

        if (!report.IsAsSubmitted)
            throw new ShiftEntityException(new Message(
                loc["Error"],
                loc["This access tree has changes that you can not make"],
                new[]
                {
                    (Title: loc["You can not grant"].Value, Lines: report.Dropped),
                    (Title: loc["You can not remove"].Value, Lines: report.Kept),
                }
                .Where(x => x.Lines.Count > 0)
                .Select(x => new Message(x.Title, null!, x.Lines.Select(y => new Message(y.Name, y.Description)).ToList()))
                .ToList()));

        return report.AccessTree;
    }
}
