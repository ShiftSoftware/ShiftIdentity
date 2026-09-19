using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Services;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Connects the legacy administrator writers to the staged authority. Registered only where the admission services
/// are registered; never by the production defaults.
/// </summary>
internal sealed class AdmissionUserAccountAuthority(IdentityAdmissionServices services, IHttpContextAccessor http,
    IServiceScopeFactory scopes, ShiftIdentityLocalizer? localizer = null) : IUserAccountAuthority
{
    public string? CheckNewPassword(string password, string username) =>
        services.PasswordPolicy.Validate(password, username) is { } failure ? NewPasswordPolicy.Describe(failure) : null;

    public async Task AdmitAsync(ShiftIdentityDbContext db, IReadOnlyList<UserAccountChange> changes, CancellationToken cancellationToken)
    {
        var actor = AccountSecurityService.ReadActor(services, http.HttpContext)
            ?? throw Translate(new AdmissionRefusedException(AuthenticationFailure.InvalidGrant, AdmissionRefusalReason.Operator));
        try
        {
            await AccountSecurityService.AdmitLegacyAsync(services, db, actor, changes, cancellationToken);
        }
        catch (AdmissionRefusedException refused) { throw Translate(refused); }
    }

    public async Task RegisterCreatedAsync(ShiftIdentityDbContext db, IReadOnlyList<UserAccountCreation> created, CancellationToken cancellationToken)
    {
        var actor = AccountSecurityService.ReadActor(services, http.HttpContext)
            ?? throw Translate(new AdmissionRefusedException(AuthenticationFailure.InvalidGrant, AdmissionRefusalReason.Operator));
        try
        {
            await AccountSecurityService.RegisterCreatedAsync(services, db, actor, created, cancellationToken);
        }
        catch (AdmissionRefusedException refused) { throw Translate(refused); }
    }

    public async Task<UserAccountDelivery> RequestVerificationAsync(long userID, CancellationToken cancellationToken)
    {
        // A fresh scope: the request's own context has just committed and must not be cleared by another admission.
        using var scope = scopes.CreateScope();
        var fresh = scope.ServiceProvider.GetRequiredService<IdentityAdmissionServices>();
        var actor = AccountSecurityService.ReadActor(fresh, http.HttpContext);
        if (actor is null) return UserAccountDelivery.NotRequested;
        var outcome = await AccountSecurityService.AdminSecurityLinkAsync(fresh, actor, userID, AuthenticationOperationPurpose.EmailVerify, cancellationToken);
        return outcome switch
        {
            SecurityDeliveryRequested => UserAccountDelivery.Requested,
            AuthenticationRefused { Code: AuthenticationFailure.Unavailable } => UserAccountDelivery.Unconfirmed,
            _ => UserAccountDelivery.NotRequested
        };
    }

    private ShiftEntityException Translate(AdmissionRefusedException refused)
    {
        string Text(string key) => localizer is null ? key : localizer[key];
        var (status, title, body) = refused.Reason switch
        {
            AdmissionRefusalReason.Self => (HttpStatusCode.Forbidden, "Forbidden", "Use your own profile to change your own account."),
            AdmissionRefusalReason.Protected => (HttpStatusCode.Forbidden, "Forbidden", "Built-In Data can't be modified."),
            AdmissionRefusalReason.Deleted => (HttpStatusCode.NotFound, "Not Found", "User not found"),
            AdmissionRefusalReason.Stale => (HttpStatusCode.Conflict, "Conflict", "This user changed meanwhile. Reload the user and try again."),
            AdmissionRefusalReason.Duplicate => (HttpStatusCode.BadRequest, "Duplicate", refused.Field == nameof(Data.Entities.User.Email)
                ? "Another account already uses this email." : "Another account already uses this username."),
            AdmissionRefusalReason.Invalid => (HttpStatusCode.BadRequest, "Validation Error", refused.Field == nameof(Data.Entities.User.Email)
                ? "Invalid Email Address" : "Check the entered value and try again."),
            AdmissionRefusalReason.Unavailable => (HttpStatusCode.ServiceUnavailable, "Error", "The change could not be saved. Please wait and try again."),
            _ => refused.Code switch
            {
                AuthenticationFailure.InvalidGrant => (HttpStatusCode.Unauthorized, "Unauthorized", "Sign in again before changing this account."),
                AuthenticationFailure.InvalidProof => (HttpStatusCode.Forbidden, "Forbidden", "Your sign-in is too old for this change. Sign in again and retry."),
                AuthenticationFailure.ReauthenticationRequired => (HttpStatusCode.Forbidden, "Confirm your identity", "Confirm your password and applicable MFA to continue this change."),
                AuthenticationFailure.AccountUnavailable => (HttpStatusCode.Forbidden, "Forbidden", "Your account is not available for this change."),
                AuthenticationFailure.Unavailable => (HttpStatusCode.ServiceUnavailable, "Error", "The change could not be saved. Please wait and try again."),
                _ => (HttpStatusCode.Forbidden, "Forbidden", "You do not have permission to change this account.")
            }
        };
        return new ShiftEntityException(new Message(Text(title), Text(body))
        {
            For = refused.Code == AuthenticationFailure.ReauthenticationRequired ? AdministratorAuthentication.RequiredMessage : refused.Field
        }, (int)status);
    }
}
