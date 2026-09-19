namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>A rendered security email. The destination and content come from the admitted snapshot.</summary>
public sealed record SecurityEmailContent(Guid ID, string Destination, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// A host delivery provider for both verification and reset messages. The host chooses its transport or SDK.
/// Return promptly with a Task, honor cancellation, and await the delivery system's acceptance (including queue
/// acceptance for a host-owned messaging service). Acceptance does not mean inbox arrival. Never log the message or
/// start unawaited delivery. Registered staged providers take precedence over the old ISendEmailVerification/
/// ISendEmailResetPassword providers; every staged provider must accept the message.
/// Hosts that need their own composition can still replace ISecurityEmailSink.
/// </summary>
public interface ISecurityEmailSender
{
    Task SendAsync(SecurityEmailContent message, CancellationToken cancellationToken);
}
