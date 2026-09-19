using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public interface IAdministratorConfirmation
{
    /// <summary>Returns the confirmed session's access token, or null on cancellation or failure.</summary>
    Task<string?> ConfirmAsync(string currentAccess, CancellationToken cancellationToken);
}

/// <summary>One continuation after an explicit refusal that guarantees the action did not commit.</summary>
public sealed class AdministratorActionContinuation(IdentitySession session, IAdministratorConfirmation confirmation, Uri authorityRoot)
{
    private readonly HashSet<string> pending = [];
    private int confirming;

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        if (!IsAdministratorAction(request)) return await send(request, ct);
        // Freeze the submitted form and selection before opening a dialog. Never rebuild them after confirmation.
        var bytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
        var key = request.Method + " " + request.RequestUri + " " + Convert.ToHexString(SHA256.HashData(bytes ?? []));
        lock (pending)
        {
            if (!pending.Add(key)) return new(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new { Message = new { Title = "Action in progress", Body = "Wait for the current action to finish." } })
            };
        }
        try
        {
            var checkpoint = await session.ReadCheckpointAsync();
            var result = await send(request, ct);
            if (!await RequiresConfirmation(result, ct) || checkpoint.Access != request.Headers.Authorization?.Parameter ||
                !await session.MatchesAsync(checkpoint) || Interlocked.CompareExchange(ref confirming, 1, 0) != 0) return result;
            try
            {
                var access = await confirmation.ConfirmAsync(checkpoint.Access!, ct);
                if (access is null || ct.IsCancellationRequested) return result;
                var confirmed = await session.ReadCheckpointAsync();
                if (confirmed.Access != access) return result;
                using var retry = Clone(request, bytes);
                retry.Headers.Authorization = new("Bearer", access);
                if (!await session.MatchesAsync(confirmed)) return result;
                // Send once. A second refusal, timeout, or lost response is returned without another replay.
                result.Dispose();
                return await send(retry, ct);
            }
            finally { Volatile.Write(ref confirming, 0); }
        }
        finally { lock (pending) pending.Remove(key); }
    }

    private bool IsAdministratorAction(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null || !uri.IsAbsoluteUri || !authorityRoot.IsBaseOf(uri) ||
            request.Headers.Authorization?.Scheme != "Bearer" || request.Method == HttpMethod.Get) return false;
        var path = authorityRoot.MakeRelativeUri(uri).OriginalString.Split('?')[0].TrimEnd('/');
        return path.Equals("api/IdentityUser", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("api/IdentityUser/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("api/identity/v2/admin/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("api/identity/v2/mfa/recovery-code", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> RequiresConfirmation(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden) return false;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (Property(root, "Message", out var message) && Property(message, "For", out var code) &&
                code.ValueKind == JsonValueKind.String && code.GetString() == AdministratorAuthentication.RequiredMessage) return true;
            return Property(root, "kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "refused" &&
                Property(root, "code", out var failure) &&
                ((failure.ValueKind == JsonValueKind.Number && failure.TryGetInt32(out var number) && number == (int)AuthenticationFailure.ReauthenticationRequired) ||
                 (failure.ValueKind == JsonValueKind.String && failure.GetString() == nameof(AuthenticationFailure.ReauthenticationRequired)));
        }
        catch (JsonException) { return false; }
    }

    private static bool Property(JsonElement value, string name, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var item in value.EnumerateObject())
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) { property = item.Value; return true; }
        property = default; return false;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? bytes)
    {
        var request = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version, VersionPolicy = source.VersionPolicy };
        foreach (var header in source.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in source.Options) request.Options.TryAdd(option.Key, option.Value);
        if (bytes is not null)
        {
            request.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content!.Headers) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return request;
    }
}

public sealed class AdministratorActionHandler(AdministratorActionContinuation continuation) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        continuation.SendAsync(request, (message, ct) => base.SendAsync(message, ct), cancellationToken);
}
