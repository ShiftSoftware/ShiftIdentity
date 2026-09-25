using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Composes staged email for registered providers. The old provider fallback is temporary until Phase 6;
/// it never invokes the legacy SAS-grant generator.
/// </summary>
internal sealed class HostSecurityEmailSink(ShiftIdentityConfiguration configuration,
    IEnumerable<ISendEmailVerification> verification, IEnumerable<ISendEmailResetPassword> reset,
    IEnumerable<ISecurityEmailSender>? senders = null) : ISecurityEmailSink
{
    private readonly ISecurityEmailSender[] staged = senders?.ToArray() ?? [];

    /// <summary>Why this adapter cannot send, or null when it can. A send refuses with it; startup only logs it.</summary>
    internal string? Problem()
    {
        var missing = new List<string>();
        if (staged.Length == 0 && !verification.Any()) missing.Add(nameof(ISendEmailVerification));
        if (staged.Length == 0 && !reset.Any()) missing.Add(nameof(ISendEmailResetPassword));
        if (missing.Count > 0)
            return "The identity authority requires an ISecurityEmailSink or its host sender adapters. Missing: " + string.Join(", ", missing) + ".";
        try { SecurityEmailTemplate.ValidateFrontEndUrl(configuration.FrontEndUrl); }
        catch (InvalidOperationException e) { return e.Message; }
        return null;
    }

    internal void CheckReady()
    {
        if (Problem() is { } problem) throw new InvalidOperationException(problem);
    }

    public async Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken)
    {
        CheckReady();
        cancellationToken.ThrowIfCancellationRequested();
        if (staged.Length > 0)
        {
            var content = SecurityEmailTemplate.Render(message, configuration.FrontEndUrl!);
            foreach (var sender in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sender.SendAsync(content, cancellationToken).WaitAsync(cancellationToken);
            }
            return;
        }
        var link = SecurityEmailTemplate.Link(message, configuration.FrontEndUrl!);
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
