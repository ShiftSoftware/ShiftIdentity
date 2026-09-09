using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

public sealed record SecurityEmail(Guid ID, string Destination, string Subject, string Grant,
    AuthenticationOperationPurpose Purpose, DateTimeOffset ExpiresAt);

/// <summary>
/// One awaited handoff. Normal return means the host accepted responsibility; an exception or
/// cancellation means acceptance is unconfirmed. Honor cancellation, bound external I/O,
/// and return a Task without blocking synchronously.
/// The host owns downstream delivery and any retries. ID is the operation ID and may be used
/// for host deduplication. Do not log the grant. The framework never retries this call.
/// </summary>
public interface ISecurityEmailSink
{
    Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken);
}
