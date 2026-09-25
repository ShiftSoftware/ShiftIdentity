using Microsoft.Extensions.DependencyInjection;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// The admission services' sink: it resolves the host's registered sink, and so builds the host's senders, only when a
/// security email is sent. A sender that cannot be built fails that send inside the handoff, which cancels its grant.
/// </summary>
internal sealed class DeferredSecurityEmailSink(IServiceProvider services) : ISecurityEmailSink
{
    public async Task DeliverAsync(SecurityEmail message, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ISecurityEmailSink>().DeliverAsync(message, cancellationToken);
}
