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
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung C): User's base CRUD is attribute-driven through the surviving UserRepository
// (which stays because it's IUserRepository + holds the public methods the custom endpoints/auth flows call). The
// heavy upsert (uniqueness, CompanyBranch→Region/Country/Company derivation, TypeAuth access-tree generation +
// grant validation, password hashing, verification-flag resets, UserAccessTrees M:N sync) moved here into the
// IUpsertsShiftRepository hook; the mapper config lives in the repo's base-ctor builder. Feature-lock + protected
// guard are central (Phase 0). The 6 custom endpoints are sibling minimal APIs (see UserEndpoints).
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
    IUpsertsShiftRepository<User, UserListDTO, UserDTO>
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

    // Ported verbatim from the old UserRepository.UpsertAsync (feature-lock + protected guard dropped — central).
    // All work happens on `entity` BEFORE context.Base(): Base() maps the convention scalars (Username/IsActive/
    // FullName/BirthDate) + IntegrationId (ForEntity), audit-stamps, runs the protected-row guard, and the
    // company-scoped data-level write check — which authorizes against the CompanyBranch-derived scope set below.
    // Email/Phone/AccessTree are IgnoreEntity'd in the mapper so the values assigned here survive Base().
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
        var configuration = context.Services.GetRequiredService<ShiftIdentityConfiguration>();
        var Loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();

        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Username.ToLower() == dto.Username.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The username {0} exist", dto.Username]));

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
                throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The email {0} exist", dto.Email]));
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
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));

            formattedPhone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
        }

        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Phone.ToLower() == (formattedPhone ?? "").ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The phone {0} exist", dto.Phone]));

        if (actionType == ActionTypes.Insert)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Password can not be empty."]));
        }

        // Capture old values before mutation to reset verification flags on change
        var oldEmail = entity.Email;
        var oldPhone = entity.Phone;

        // Email/Phone are IgnoreEntity'd (trim/format owned here); Username/IsActive/FullName/BirthDate/IntegrationId
        // are handled by Base()'s MapToEntity.
        entity.Email = dto.Email;
        entity.Phone = formattedPhone;

        // Reset verification flags when email or phone changes (replaces ResetUserTrigger)
        if (actionType == ActionTypes.Update)
        {
            if (!string.Equals(entity.Email, oldEmail, StringComparison.OrdinalIgnoreCase))
            {
                entity.EmailVerified = false;
                entity.VerificationSASToken = null;
            }

            if (!string.Equals(entity.Phone, oldPhone, StringComparison.OrdinalIgnoreCase))
            {
                entity.PhoneVerified = false;
            }
        }

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

        entity.AccessTree = typeAuth_Producer.GenerateAccessTree((typeAuthService as TypeAuthContext)!, typeAuth_Preserver);

        if (!string.IsNullOrEmpty(dto.Password))
        {
            var hash = HashService.GenerateHash(dto.Password);

            entity.PasswordHash = hash.PasswordHash;
            entity.Salt = hash.Salt;

            // Same source AssignRandomPasswords uses
            entity.RequireChangePassword = configuration.Security.RequirePasswordChange;
        }

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

        db.UserAccessTrees.RemoveRange(removedAccessTrees);
        await db.UserAccessTrees.AddRangeAsync(addedAccessTrees.Select(x => new UserAccessTree
        {
            AccessTreeID = x,
            User = entity
        }));

        // Base(): MapToEntity (Username/IsActive/FullName/BirthDate + IntegrationId ForEntity), audit, protected-row
        // guard, company-scoped data-level write check (authorizes against the derived CompanyBranch scope above).
        return await context.Base();
    }

}
