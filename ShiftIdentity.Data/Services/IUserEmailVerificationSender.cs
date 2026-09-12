using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Services;

/// <summary>
/// Sends the legacy SAS email-verification link to a saved user through the same mechanism as
/// <c>api/IdentityUser/VerifyEmails</c>: the link is named after the VerifyEmail route, its token is persisted on
/// <see cref="User.VerificationSASToken"/> and the registered <c>ISendEmailVerification</c> providers deliver it.
/// The dashboard host implements it (it owns the route name and the request URL); a host without a registration
/// sends nothing. <see cref="Repositories.UserRepository"/> calls it only after its own save has committed, so the
/// user's ID is known and no repository transaction is open. An implementation must never throw: delivery problems
/// are logged and cannot fail the save that already succeeded.
/// </summary>
public interface IUserEmailVerificationSender
{
    Task SendAsync(User user);
}
