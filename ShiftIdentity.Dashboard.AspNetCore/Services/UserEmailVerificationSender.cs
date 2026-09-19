using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Core.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Mappers;
using ShiftSoftware.ShiftIdentity.Data.Services;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Services;

// The dashboard's implementation of the post-save verification send behind the User form's "Send a verification
// link to this address" choice. It reproduces UserEndpoints.VerifyEmails step by step — link named after the
// VerifyEmail route (so UserManagerEndpoints.VerifyEmail validates it), token persisted on the user, then every
// registered ISendEmailVerification provider — but runs after UserRepository has committed the user's save, so it
// never runs inside that transaction and can never fail it: every problem is logged and swallowed here.
internal sealed class UserEmailVerificationSender : IUserEmailVerificationSender
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly LinkGenerator linkGenerator;
    private readonly IHashIdService hashIdService;
    private readonly ShiftIdentityConfiguration options;
    private readonly ShiftIdentityDbContext db;
    private readonly IEnumerable<ISendEmailVerification> providers;
    private readonly ILogger<UserEmailVerificationSender> logger;
    private readonly IUserAccountAuthority? authority;

    public UserEmailVerificationSender(
        IHttpContextAccessor httpContextAccessor,
        LinkGenerator linkGenerator,
        IHashIdService hashIdService,
        ShiftIdentityConfiguration options,
        ShiftIdentityDbContext db,
        IEnumerable<ISendEmailVerification> providers,
        ILogger<UserEmailVerificationSender> logger,
        IUserAccountAuthority? authority = null)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.linkGenerator = linkGenerator;
        this.hashIdService = hashIdService;
        this.options = options;
        this.db = db;
        this.providers = providers;
        this.logger = logger;
        this.authority = authority;
    }

    public async Task SendAsync(User user)
    {
        // Temporary adapter: direct users of this legacy interface also enter the authority. Remove in Phase 6.
        if (authority is not null)
        {
            try
            {
                if (await authority.RequestVerificationAsync(user.ID, CancellationToken.None) != UserAccountDelivery.Requested)
                    logger.LogWarning("Verification delivery was not confirmed for user {UserID} after the save.", user.ID);
            }
            catch (Exception)
            {
                // A provider exception can contain its link. Never log it or fail the already committed save.
                logger.LogWarning("Verification delivery failed for user {UserID} after the save.", user.ID);
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(user.Email) || user.EmailVerified)
            return;

        string fullUrl;

        try
        {
            var httpContext = httpContextAccessor.HttpContext;

            if (httpContext is null || options.SASToken?.Key is null)
            {
                logger.LogWarning("No verification link was sent to user {UserID}: the request context or the SAS token settings are unavailable.", user.ID);
                return;
            }

            var encodedId = hashIdService.Encode<UserDTO>(user.ID);

            // Same named route and uniqueId as VerifyEmails, so VerifyEmail reconstructs an identical SAS descriptor.
            var url = linkGenerator.GetPathByName(httpContext, UserManagerEndpoints.VerifyEmailRouteName, new { userId = encodedId });

            if (url is null)
            {
                logger.LogWarning("No verification link was sent to user {UserID}: the VerifyEmail route is not mapped in this host.", user.ID);
                return;
            }

            var uniqueId = $"{url}-{user.Email}";
            var (token, expires) = TokenService.GenerateSASToken(uniqueId, encodedId,
                DateTime.UtcNow.AddSeconds(options.SASToken.ExpiresInSeconds), options.SASToken.Key);

            string baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            fullUrl = $"{baseUrl}{(baseUrl.EndsWith('/') ? baseUrl.Substring(0, baseUrl.Length - 1) : "")}{url}?expires={expires}&token={token}";

            // The link only validates against the persisted token; this is a separate small save after the
            // user's own save has committed.
            user.VerificationSASToken = token;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The verification link for user {UserID} could not be issued after the save; the user was saved without it.", user.ID);
            return;
        }

        foreach (var provider in providers)
        {
            try
            {
                await provider.SendEmailVerificationAsync(fullUrl, user.ToDataDTO());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A verification email provider failed for user {UserID}; the user was saved and the link stays valid.", user.ID);
            }
        }
    }
}
