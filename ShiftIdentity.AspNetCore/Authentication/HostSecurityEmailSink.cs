using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Temporary delivery adapter for hosts using the existing sender interfaces. Remove with those interfaces in
/// Phase 6. It delivers staged grants only; it never invokes the legacy SAS-grant generator.
/// </summary>
internal sealed class HostSecurityEmailSink(ShiftIdentityConfiguration configuration,
    IEnumerable<ISendEmailVerification> verification, IEnumerable<ISendEmailResetPassword> reset) : ISecurityEmailSink
{
    internal void CheckReady()
    {
        var missing = new List<string>();
        if (!verification.Any()) missing.Add(nameof(ISendEmailVerification));
        if (!reset.Any()) missing.Add(nameof(ISendEmailResetPassword));
        if (missing.Count > 0)
            throw new InvalidOperationException("The identity authority requires an ISecurityEmailSink or its host sender adapters. Missing: " + string.Join(", ", missing) + ".");
        if (configuration.FrontEndUrl is not { } url || !IdentityAuthorityRegistration.IsWebUrl(url)
            || new Uri(url).Query.Length != 0 || new Uri(url).Fragment.Length != 0)
            throw new InvalidOperationException("The identity authority's host sender adapter requires ShiftIdentityConfiguration.FrontEndUrl: an absolute HTTP(S) dashboard URL without a query or fragment.");
    }

    public async Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken)
    {
        CheckReady();
        var page = message.Purpose switch
        {
            AuthenticationOperationPurpose.EmailVerify => "VerifyEmail",
            AuthenticationOperationPurpose.PasswordResetEmail => "ResetPassword",
            _ => throw new InvalidOperationException("This purpose cannot be delivered by email.")
        };
        var link = configuration.FrontEndUrl!.TrimEnd('/') + "/Identity/" + page
            + "#grant=" + Uri.EscapeDataString(message.Grant) + "&purpose=" + message.Purpose;
        // The recipient and display fields are the snapshot admitted with this grant, never a later database lookup
        // or caller-supplied address. The legacy interfaces lack cancellation; stop awaiting when the budget ends.
        UserDataDTO Recipient() => new()
        {
            ID = message.UserID, Email = message.Destination, Username = message.Username, FullName = message.FullName
        };
        if (message.Purpose == AuthenticationOperationPurpose.EmailVerify)
            foreach (var sender in verification)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sender.SendEmailVerificationAsync(link, Recipient()).WaitAsync(cancellationToken);
            }
        else
            foreach (var sender in reset)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sender.SendEmailResetPasswordAsync(link, Recipient()).WaitAsync(cancellationToken);
            }
        // Every configured provider must return normally for the coordinator to record confirmed acceptance.
    }
}
