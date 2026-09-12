using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Services;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung C): User's base CRUD is attribute-driven through the surviving UserRepository
// (which stays because it's IUserRepository + holds the public methods the custom endpoints/auth flows call). The
// heavy upsert (uniqueness, CompanyBranch→Region/Country/Company derivation, TypeAuth access-tree generation +
// grant validation, password hashing, verification-flag resets, UserAccessTrees M:N sync) moved here into the
// IUpsertsShiftRepository hook; the mapper config lives in the repo's base-ctor builder. Feature-lock + protected
// guard are central (Phase 0). The 6 custom endpoints are sibling minimal APIs (see UserEndpoints).
//
// Two modes. When the host registers IUserAccountAuthority (the staged v2 authority), this hook never writes a
// credential, identifier, contact, status, permission or deletion flag itself: it validates, prepares the hash
// outside every lock, describes the change, and UserRepository asks the authority to admit it inside the save
// transaction (locks, current-state checks, one SecurityVersion increment, operator audit). Without that
// registration the previous direct writes remain; that is production until the authority cutover.
[TemporalShiftEntity]
[Table("Users", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<UserListDTO, UserDTO, ShiftIdentityActions, ShiftSoftware.ShiftIdentity.Data.Repositories.UserRepository>("api/IdentityUser", nameof(ShiftIdentityActions.Users))]
public class User : ShiftEntity<User>,
    IEntityHasCountry<User>,
    IEntityHasRegion<User>,
    IEntityHasCompany<User>,
    IEntityHasCompanyBranch<User>,
    IShiftEntityReplication,
    IShiftEntityProtectable,
    IUpsertsShiftRepository<User, UserListDTO, UserDTO>,
    IDeletesShiftRepository<User, UserListDTO, UserDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    #region Security

    [Required]
    [MaxLength(255)]
    public string Username { get; set; } = default!;

    public string? IntegrationId { get; set; }

    public byte[] PasswordHash { get; set; } = default!;

    public byte[] Salt { get; set; } = default!;

    public int LoginAttempts { get; set; }

    public DateTime? LockDownUntil { get; set; }

    public bool IsActive { get; set; }

    public bool IsProtected { get; set; }

    public string? AccessTree { get; set; }

    public bool RequireChangePassword { get; set; }

    public string? VerificationSASToken { get; set; }

    public byte[]? TotpSecret { get; set; }

    #endregion

    #region Contacts
    [MaxLength(255)]
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }
    public bool PhoneVerified { get; set; }
    #endregion

    #region Profile

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = default!;

    public DateTime? BirthDate { get; set; }
    #endregion

    public DateTimeOffset? LastSeen { get; set; }

    public long? CountryID { get; set; }
    public long? RegionID { get; set; }
    public long? CompanyID { get; set; }
    public long? CompanyBranchID { get; set; }
    public string? Signature { get; set; }

    public virtual Country? Country { get; set; }
    public virtual Region? Region { get; set; }
    public virtual Company? Company { get; set; }
    public virtual CompanyBranch? CompanyBranch { get; set; }
    public virtual UserLog UserLog { get; set; }

    public virtual IEnumerable<UserAccessTree> AccessTrees { get; set; }
    public virtual ICollection<TeamUser> TeamUsers { get; set; } = new HashSet<TeamUser>();

    public User(long id) : base(id)
    {
        AccessTrees = new List<UserAccessTree>();
        TeamUsers = new HashSet<TeamUser>();
    }

    public User()
    {
        AccessTrees = new List<UserAccessTree>();
        TeamUsers = new HashSet<TeamUser>();
    }

    // All work happens on `entity` BEFORE context.Base(): Base() maps the convention scalars (FullName/BirthDate) +
    // IntegrationId (ForEntity), audit-stamps, runs the protected-row guard, and the company-scoped data-level write
    // check — which authorizes against the CompanyBranch-derived scope set below. Username/IsActive/Email/Phone/
    // AccessTree are IgnoreEntity'd in the mapper: the direct path assigns them here and the authority path leaves
    // them to the admitted save, so Base() never overwrites either.
    public async ValueTask<User> UpsertAsync(
        User entity,
        UserDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<User, UserListDTO, UserDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var typeAuthService = context.Services.GetRequiredService<ITypeAuthService>();
        var Loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();
        var authority = context.Services.GetService<IUserAccountAuthority>();
        var repository = context.Repository as Repositories.UserRepository;

        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        // The authority path uses the trimmed identifier the staged username route would store; the direct path keeps
        // the value exactly as before.
        var username = authority is null ? dto.Username : (dto.Username ?? "").Trim();

        if (authority is not null && (username.Length == 0 || username.Any(char.IsControl)))
            throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Please provide", Loc["Username"]]) { For = nameof(UserDTO.Username) });

        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Username.ToLower() == username.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The username {0} exist", username]) { For = nameof(UserDTO.Username) });

        if (!string.IsNullOrWhiteSpace(dto.IntegrationId))
        {
            //Check if the integration id is duplicate
            if (await db.Users.AnyAsync(x => !x.IsDeleted && x.IntegrationId != null && x.IntegrationId.ToLower() == dto.IntegrationId.ToLower() && x.ID != id))
                throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The Integration ID {0} already exists", dto.IntegrationId]));
        }

        //Check if the email is duplicate
        dto.Email = dto.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Email!.ToLower() == dto.Email.ToLower() && x.ID != id))
            {
                throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The email {0} exist", dto.Email]) { For = nameof(UserDTO.Email) });
            }
        }
        else
        {
            dto.Email = null;
        }

        //Check if the phone is duplicate
        string? formattedPhone = null;
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]) { For = nameof(UserDTO.Phone) });

            formattedPhone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
        }

        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Phone.ToLower() == (formattedPhone ?? "").ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The phone {0} exist", dto.Phone]) { For = nameof(UserDTO.Phone) });

        if (actionType == ActionTypes.Insert)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Password can not be empty."]) { For = nameof(UserDTO.Password) });
        }

        // The staged authority applies the shared new-password policy; failing early keeps the save from hashing.
        if (authority is not null && !string.IsNullOrEmpty(dto.Password) && authority.CheckNewPassword(dto.Password, username) is { } policyFailure)
            throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc[policyFailure]) { For = nameof(UserDTO.Password) });

        // Capture old values before mutation to reset verification flags on change
        var oldEmail = entity.Email;
        var oldPhone = entity.Phone;
        var emailChanged = actionType == ActionTypes.Update && !string.Equals(dto.Email, oldEmail, StringComparison.OrdinalIgnoreCase);
        var phoneChanged = actionType == ActionTypes.Update && !string.Equals(formattedPhone, oldPhone, StringComparison.OrdinalIgnoreCase);

        // A verification link goes to a NEW address only: the address of a created user, or a changed address on an
        // update — and only when the administrator left the form's "send a verification link" choice on.
        var sendVerification = dto.SendVerification && dto.Email is not null && (actionType == ActionTypes.Insert || emailChanged);

        //if (dto.CompanyBranchID != null)
        {
            entity.CompanyBranchID = dto.CompanyBranchID!.Value.ToLong();

            var companyBranch = await db.CompanyBranches
                .Include(x => x.Region)
                .FirstOrDefaultAsync(x => x.ID == entity.CompanyBranchID);

            entity.CountryID = companyBranch!.Region?.CountryID;
            entity.RegionID = companyBranch!.RegionID!.Value;

            entity.CompanyID = companyBranch.CompanyID!.Value;
        }

        var typeAuthContextBuilder_Producer = new TypeAuthContextBuilder();
        var typeAuthContextBuilder_Preserver = new TypeAuthContextBuilder();

        TypeAuthContext typeAuth_Producer;
        TypeAuthContext? typeAuth_Preserver = null;

        foreach (var type in typeAuthService.GetRegisteredActionTrees())
        {
            typeAuthContextBuilder_Producer.AddActionTree(type);
            typeAuthContextBuilder_Preserver.AddActionTree(type);
        }

        typeAuthContextBuilder_Producer.AddAccessTree(dto.AccessTree!);

        if (entity.ID != default)
        {
            typeAuthContextBuilder_Preserver.AddAccessTree(entity.AccessTree!);
            typeAuth_Preserver = typeAuthContextBuilder_Preserver.Build();
        }

        typeAuth_Producer = typeAuthContextBuilder_Producer.Build();

        var generatedAccessTree = typeAuth_Producer.GenerateAccessTree((typeAuthService as TypeAuthContext)!, typeAuth_Preserver);

        // The expensive hash runs here, before the save transaction and outside every lock. The staged authority
        // stores the versioned format; the direct path keeps the format the legacy login verifies.
        HashModel? hash = string.IsNullOrEmpty(dto.Password) ? null
            : authority is null ? HashService.GenerateHash(dto.Password) : HashService.GenerateVersionedHash(dto.Password);

        var accessTreeIds = dto.AccessTrees.Select(x => x.Value.ToLong());

        var trees = await db.AccessTrees.Where(x => accessTreeIds.Contains(x.ID)).ToDictionaryAsync(x => x.ID, x => x);

        var inaccessibleAccessTress = new Dictionary<AccessTree, Dictionary<TypeAuth.Core.Actions.ActionBase, string>>();

        foreach (var tree in trees.Values)
        {
            var tAuthBuilderForThisTree = new TypeAuthContextBuilder();

            foreach (var type in typeAuthService.GetRegisteredActionTrees())
            {
                tAuthBuilderForThisTree.AddActionTree(type);
            }

            tAuthBuilderForThisTree.AddAccessTree(tree.Tree);

            var tAuthForThisTree = tAuthBuilderForThisTree.Build();

            var inAccessibleActions = (typeAuthService as TypeAuthContext)!.FindInAccessibleActionsOn(tAuthForThisTree);

            if (inAccessibleActions.Count > 0)
            {
                inaccessibleAccessTress[tree] = inAccessibleActions;
            }
        }

        if (inaccessibleAccessTress.Count > 0)
        {
            throw new ShiftEntityException(new Message(
                Loc["Error"],
                Loc["Below Access Trees contain accesses that you can not grant"],
                inaccessibleAccessTress.Select(x => new Message(x.Key.Name, null!, x.Value.Select(y => new Message(y.Key.Name!, y.Value.ToString()!)).ToList())).ToList()
            ));
        }

        var removedAccessTrees = entity.AccessTrees.Where(x => !accessTreeIds.Contains(x.AccessTreeID)).ToList();
        var addedAccessTrees = accessTreeIds.Where(x => !entity.AccessTrees.Any(y => y.AccessTreeID == x)).ToList();

        // Any difference in the effective permissions counts as a change. Reductions must end the user's sessions;
        // additions are treated the same way because the shared comparer cannot see per-row data-level access, so a
        // precise split could miss a reduction.
        var permissionsChanged = actionType == ActionTypes.Update &&
            (removedAccessTrees.Count > 0 || addedAccessTrees.Count > 0 || !AccessTreeComparison.Equivalent(entity.AccessTree, generatedAccessTree));

        if (authority is null || actionType == ActionTypes.Insert)
        {
            // Direct writes: the legacy path, and a new row in both modes. A new row has no security state yet; in the
            // authority mode the repository records that state after the row exists, in the same transaction.
            entity.Username = username;
            entity.IsActive = dto.IsActive;
            entity.Email = dto.Email;
            entity.Phone = formattedPhone;

            // Reset verification flags when email or phone changes (replaces ResetUserTrigger)
            if (emailChanged)
            {
                entity.EmailVerified = false;
                entity.VerificationSASToken = null;
            }

            if (phoneChanged)
                entity.PhoneVerified = false;

            entity.AccessTree = generatedAccessTree;

            if (hash is not null)
            {
                entity.PasswordHash = hash.PasswordHash;
                entity.Salt = hash.Salt;

                // The administrator's per-save choice from the form (checked by default). The flag is left untouched
                // when no password is supplied.
                entity.RequireChangePassword = dto.RequireChangeAtNextLogin;
            }
        }

        db.UserAccessTrees.RemoveRange(removedAccessTrees);
        await db.UserAccessTrees.AddRangeAsync(addedAccessTrees.Select(x => new UserAccessTree
        {
            AccessTreeID = x,
            User = entity
        }));

        // Base(): MapToEntity (FullName/BirthDate + IntegrationId ForEntity), audit, protected-row guard, company-scoped
        // data-level write check (authorizes against the derived CompanyBranch scope above).
        var saved = await context.Base();

        if (authority is not null && repository is not null)
        {
            if (actionType == ActionTypes.Update)
            {
                var change = new UserAccountChange
                {
                    User = saved,
                    Password = hash,
                    RequireChangeAtNextLogin = dto.RequireChangeAtNextLogin,
                    Username = string.Equals(saved.Username, username, StringComparison.Ordinal) ? null : username,
                    EmailChanged = emailChanged,
                    Email = dto.Email,
                    PhoneChanged = phoneChanged,
                    Phone = formattedPhone,
                    IsActive = saved.IsActive == dto.IsActive ? null : dto.IsActive,
                    PermissionsChanged = permissionsChanged,
                    AccessTree = generatedAccessTree
                };

                if (!change.IsEmpty)
                    repository.RequireAdmission(change);
            }
            else
            {
                repository.RequireCreation(new UserAccountCreation(saved));
            }

            // The staged delivery path issues the link after the commit; the legacy SAS sender is not used here.
            if (sendVerification)
                repository.RunAfterSave(() => repository.ReportVerificationAsync(authority, saved.ID));
        }
        else if (sendVerification
            && repository is not null
            && context.Services.GetService<IUserEmailVerificationSender>() is { } sender)
        {
            // This hook runs before SaveChanges, so it must not send. The link needs the committed row (a created user
            // has no ID yet), so the repository sends it after its save has committed, outside the transaction. A host
            // without the dashboard sender registered sends nothing, exactly like VerifyEmails without providers.
            repository.RunAfterSave(() => sender.SendAsync(saved));
        }

        return saved;
    }

    // The default delete keeps its guard, data-level check, soft-delete flag and audit stamp. With the staged
    // authority registered the deletion is admitted in the save transaction as well: the operator needs Users delete
    // permission from current state, the target's SecurityVersion is incremented and the deletion is audited.
    public async ValueTask<User> DeleteAsync(
        User entity,
        long? userId,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryDeleteContext<User, UserListDTO, UserDTO> context)
    {
        var deleted = await context.Base();

        if (context.Services.GetService<IUserAccountAuthority>() is not null && context.Repository is Repositories.UserRepository repository)
            repository.RequireAdmission(new UserAccountChange { User = deleted, Delete = true });

        return deleted;
    }
}
