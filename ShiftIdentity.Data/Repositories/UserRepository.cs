using ShiftSoftware.ShiftIdentity.Data.Mappers;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data.Services;
using ShiftSoftware.TypeAuth.Core;
using System.Data;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// THIN(ish) repository (Rung C): User's base CRUD is attribute-driven (the [ShiftEntitySecureEndpoint] on the User
// entity routes here). The repo SURVIVES because it's IUserRepository and holds the public methods the custom
// endpoints (UserEndpoints) + auth/account flows call. The heavy upsert moved to the User entity's
// IUpsertsShiftRepository hook; the mapper config lives in the base-ctor builder below. No UpsertAsync/DeleteAsync
// overrides — feature-lock + protected guard are central (Phase 0).
//
// With the staged authority registered (IUserAccountAuthority), every sensitive change collected by the hooks and
// the bulk methods is admitted inside this repository's save transaction, just before the flush. One transaction
// then commits the ordinary edits, the security changes, the version increments and the audit rows together.
public class UserRepository :
    ShiftRepository<ShiftIdentityDbContext, User, UserListDTO, UserDTO>,
    IUserRepository
{
    private readonly ITypeAuthService typeAuthService;
    private readonly ShiftIdentityLocalizer Loc;
    private readonly ShiftIdentityConfiguration configuration;
    private readonly IUserAccountAuthority? authority;

    // Work the upsert hook defers until the save has committed (the verification email for a new address). It runs
    // after base.SaveChangesAsync() returns, i.e. outside the repository transaction, and is discarded if the save
    // throws. Each item is responsible for its own failure handling; nothing here can fail an already committed save.
    private readonly List<Func<Task>> afterSaveWork = [];

    // Security changes waiting for admission in the next save, and rows created by that save whose security state
    // must be recorded once their IDs exist. Both are cleared whether the save commits or fails.
    private readonly List<UserAccountChange> pendingChanges = [];
    private readonly List<UserAccountCreation> pendingCreations = [];

    public UserRepository(ShiftIdentityDbContext db,
        ITypeAuthService typeAuthService,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
        ShiftIdentityLocalizer Loc,
        ShiftIdentityConfiguration configuration,
        IUserAccountAuthority? authority = null) : base(db, r =>
    {
        r.IncludeRelatedEntitiesWithFindAsync(
            x => x.Include(y => y.AccessTrees).ThenInclude(y => y.AccessTree),
            x => x.Include(y => y.UserLog),
            x => x.Include(y => y.TeamUsers),
            x => x.Include(y => y.CompanyBranch),
            x => x.Include(y => y.Company)
        );

        r.Mapping(m =>
        {
            // ── VIEW ── (the select convention can't fill CompanyBranchID: the DTO member is already named …ID, so
            // the convention appends another ID and misses — provide it explicitly, like the old profile map)
            m.View.ForMember(d => d.CompanyBranchID, opt => opt.MapFrom(e => new ShiftEntitySelectDTO { Value = e.CompanyBranchID.ToString()!, Text = e.CompanyBranch != null ? e.CompanyBranch.Name : null }));
            m.View.ForMember(d => d.TotpEnabled, opt => opt.MapFrom(e => e.TotpSecret != null));
            // AccessTrees reads through an explicit junction row, so the element convention does not reach it.
            m.View.ForMember(d => d.AccessTrees, opt => opt.MapFrom(e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name }).ToList()));
            m.View.ForMember(d => d.Password, opt => opt.Ignore()); // write-only; no entity source
            m.View.ForMember(d => d.RequireChangeAtNextLogin, opt => opt.Ignore()); // per-save form choice; no entity source, keeps its default (on)
            m.View.ForMember(d => d.SendVerification, opt => opt.Ignore()); // per-save form choice; no entity source, keeps its default (on)

            // ── ENTITY (write) ── Base() maps FullName/BirthDate; the hook owns Username/IsActive/Email/Phone/
            // AccessTree/password/CompanyBranch-derivation/UserAccessTrees (or hands them to the staged authority), so
            // those are ignored (or customized) here.
            m.Entity.ForMember(e => e.IntegrationId, opt => opt.MapFrom(dto => string.IsNullOrWhiteSpace(dto.IntegrationId) ? null : dto.IntegrationId));
            m.Entity.ForMember(e => e.Username, opt => opt.Ignore());
            m.Entity.ForMember(e => e.IsActive, opt => opt.Ignore());
            m.Entity.ForMember(e => e.Email, opt => opt.Ignore());
            m.Entity.ForMember(e => e.Phone, opt => opt.Ignore());
            m.Entity.ForMember(e => e.AccessTree, opt => opt.Ignore());
            m.Entity.ForMember(e => e.AccessTrees, opt => opt.Ignore()); // the M:N rows: the hook (or the authority) writes them

            // ── LIST ── flattened CompanyBranch name, TotpEnabled, LastSeen (UserLog fallback), and the
            // AccessTrees M:N projection. The scope-ids CompanyBranchID/CompanyID need nothing — their names
            // match the entity's, and long?→string is a standard conversion.
            m.List.ForMember(d => d.CompanyBranch, opt => opt.MapFrom(e => e.CompanyBranch != null ? e.CompanyBranch.Name : null));
            m.List.ForMember(d => d.TotpEnabled, opt => opt.MapFrom(e => e.TotpSecret != null));
            m.List.ForMember(d => d.LastSeen, opt => opt.MapFrom(e => ((e.UserLog == null || e.UserLog.LastSeen == null) ? e.LastSeen : e.UserLog.LastSeen) ?? default));
            m.List.ForMember(d => d.AccessTrees, opt => opt.MapFrom(e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name }).ToList()));
        });
    })
    {
        this.typeAuthService = typeAuthService;
        this.Loc = Loc;
        this.configuration = configuration;
        this.authority = authority;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    /// <summary>True when this host registered the staged authority; sensitive changes are then admitted, never written directly.</summary>
    public bool UsesAuthority => authority is not null;

    /// <summary>Requests one admitted verification grant, outside the repository transaction. The bulk route keeps each recipient's result.</summary>
    public Task<UserAccountDelivery> RequestVerificationAsync(long userID, CancellationToken cancellationToken) =>
        (authority ?? throw new InvalidOperationException("The identity authority is required."))
            .RequestVerificationAsync(userID, cancellationToken);

    /// <summary>
    /// Registers work that runs once the next <see cref="SaveChangesAsync"/> has committed — outside the repository
    /// transaction. Used by the User upsert hook, which runs before the save and therefore cannot send anything
    /// itself. The work is dropped when the save fails and must handle its own errors.
    /// </summary>
    public void RunAfterSave(Func<Task> work) => afterSaveWork.Add(work);

    /// <summary>Queues a sensitive change for admission by the staged authority in the next save.</summary>
    public void RequireAdmission(UserAccountChange change)
    {
        if (authority is null)
            throw new InvalidOperationException("The staged authority is not registered in this host.");

        pendingChanges.Add(change);
    }

    /// <summary>Queues a created user whose security state the staged authority records in the next save.</summary>
    public void RequireCreation(UserAccountCreation creation)
    {
        if (authority is null)
            throw new InvalidOperationException("The staged authority is not registered in this host.");

        pendingCreations.Add(creation);
    }

    /// <summary>
    /// After the commit: requests the verification link through the staged delivery path and reports the outcome to
    /// the caller through the response envelope, so the form learns whether the host accepted the message.
    /// </summary>
    public async Task ReportVerificationAsync(IUserAccountAuthority authority, long userID)
    {
        var outcome = await authority.RequestVerificationAsync(userID, CancellationToken.None);
        AdditionalResponseData ??= new Dictionary<string, object>();
        AdditionalResponseData["EmailVerification"] = outcome.ToString();
    }

    /// <summary>
    /// Builds the user's effective/combined access tree by unioning their user-specific
    /// access (<see cref="User.AccessTree"/>) with every assigned access tree, then serializing
    /// the merged result to a single access-tree JSON string. Passing the combined context as its
    /// own reducer means no reduction is applied — the output is the full set TypeAuth enforces.
    /// </summary>
    public async Task<string> GenerateEffectiveAccessTreeAsync(long userId)
    {
        var user = await db.Users
            .Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .FirstOrDefaultAsync(x => x.ID == userId && !x.IsDeleted);

        if (user is null)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["User not found"]), (int)HttpStatusCode.NotFound);

        var builder = new TypeAuthContextBuilder();

        foreach (var type in typeAuthService.GetRegisteredActionTrees())
            builder.AddActionTree(type);

        if (!string.IsNullOrWhiteSpace(user.AccessTree))
            builder.AddAccessTree(user.AccessTree);

        foreach (var assigned in user.AccessTrees)
            if (!string.IsNullOrWhiteSpace(assigned.AccessTree?.Tree))
                builder.AddAccessTree(assigned.AccessTree.Tree);

        var combined = builder.Build();

        return combined.GenerateAccessTree(combined);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await db.Users.Include(x => x.UserLog).Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .Include(x => x.CompanyBranch).Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Username == username && !x.IsDeleted);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await db.Users.FirstOrDefaultAsync(x => x.Email == email && !x.IsDeleted);
    }

    public async Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId)
    {
        var user = await FindAsync(userId, null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
        if (user is null)
            return null;

        // Both credential formats verify: the legacy HMAC and the versioned adaptive hash the staged writers store.
        if (!HashService.VerifyVersionedPassword(dto.CurrentPassword, user.Salt, user.PasswordHash))
            throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Current Password is incorrect"]));

        if (dto.CurrentPassword == dto.NewPassword)
            throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["New Password can not be the same as the current password"]));


        var hash = HashService.GenerateHash(dto.NewPassword);
        user.PasswordHash = hash.PasswordHash;
        user.Salt = hash.Salt;

        //Set enforce password change flag to false
        user.RequireChangePassword = false;

        return user;
    }

    public async Task<User?> SetTotpSecret(byte[]? secret, long userId)
    {
        var user = await FindAsync(userId, null, disableGlobalFilters: true, disableDefaultDataLevelAccess: true);
        if (user is null)
            return null;

        await SetTotpSecret(secret, user);

        return user;
    }

    public Task<User?> SetTotpSecret(byte[]? secret, User user)
    {
        if (authority is null)
        {
            user.TotpSecret = secret;
            return Task.FromResult<User?>(user);
        }

        // The staged authority owns the protected factor. A new factor is activated only by the staged enrollment
        // flows, after the proofs they require; a reset is admitted in this save (operator checks, one version
        // increment, operator audit) and never touches the retained plaintext column.
        if (secret is not null)
            throw new InvalidOperationException("A host with the staged authority activates authenticators only through the admission flows.");

        if (!user.IsProtected)
            RequireAdmission(new UserAccountChange { User = user, ResetAuthenticator = true });

        return Task.FromResult<User?>(user);
    }

    public async Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId)
    {
        var user = await FindAsync(userId, null, disableGlobalFilters: true, disableDefaultDataLevelAccess: true);

        if (user is null)
            return null;

        //Check if the user is built-in
        if (user.IsProtected)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        // The mapper checks legacy identity/contact fields before applying any ordinary edit.
        // This route cannot assign a new recovery contact or change verification state.
        dto.ApplyProfileEdits(user, Loc);

        return user;
    }

    public override async Task<int> SaveChangesAsync()
    {
        int result;

        try
        {
            result = authority is null || (pendingChanges.Count == 0 && pendingCreations.Count == 0)
                ? await base.SaveChangesAsync()
                : await SaveAdmittedAsync(authority);
        }
        catch
        {
            // Nothing was committed, so nothing may be sent for it.
            afterSaveWork.Clear();
            pendingChanges.Clear();
            pendingCreations.Clear();
            throw;
        }

        if (afterSaveWork.Count == 0)
            return result;

        var work = afterSaveWork.ToArray();
        afterSaveWork.Clear();

        foreach (var item in work)
            await item();

        return result;
    }

    // One transaction: admission (locks, current-state checks, version increments, audit rows) → the ordinary flush →
    // the security state of created rows → commit. A refusal or a failure at any stage leaves nothing committed.
    private async Task<int> SaveAdmittedAsync(IUserAccountAuthority authority)
    {
        var changes = pendingChanges.ToArray();
        var creations = pendingCreations.ToArray();
        pendingChanges.Clear();
        pendingCreations.Clear();

        // A caller that already opened a transaction keeps it; otherwise this save owns one.
        var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            if (changes.Length > 0)
                await authority.AdmitAsync(db, changes, CancellationToken.None);

            var result = await base.SaveChangesAsync();

            if (creations.Length > 0)
                await authority.RegisterCreatedAsync(db, creations, CancellationToken.None);

            if (owned is not null)
                await owned.CommitAsync();

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A row this save relied on changed after it was loaded; the locks make this rare, and nothing was kept.
            throw new ShiftEntityException(new Message(Loc["Conflict"], Loc["This user changed meanwhile. Reload the user and try again."]), (int)HttpStatusCode.Conflict);
        }
        catch (IdentitySecurityConflictException)
        {
            throw new ShiftEntityException(new Message(Loc["Conflict"], Loc["This user changed meanwhile. Reload the user and try again."]), (int)HttpStatusCode.Conflict);
        }
        catch (IdentitySecurityUnavailableException)
        {
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["The change could not be saved. Please wait and try again."]), (int)HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            if (owned is not null)
                await owned.DisposeAsync();
        }
    }

    public IEnumerable<UserInfoDTO> AssignRandomPasswords(List<User> users, int passwordLength, bool enforceChange)
    {
        var userInfos = new List<UserInfoDTO>();

        // The staged authority applies the shared new-password policy, so a generated password must satisfy it.
        if (authority is not null && passwordLength < NewPasswordPolicy.MinimumLength)
            throw new ShiftEntityException(new Message(Loc["Validation Error"],
                Loc["The password length must be at least {0}", NewPasswordPolicy.MinimumLength]));

        foreach (var user in users)
        {
            if (user.IsProtected)
                continue;

            var password = PasswordGenerator.GeneratePassword(passwordLength);

            if (authority is null)
            {
                var hash = HashService.GenerateHash(password);

                user.PasswordHash = hash.PasswordHash;
                user.Salt = hash.Salt;

                //Set flag to enforce password change
                user.RequireChangePassword = enforceChange;
            }
            else
            {
                // Hashed here, outside the save transaction; the credential is written only when the save admits it.
                RequireAdmission(new UserAccountChange
                {
                    User = user,
                    Password = HashService.GenerateVersionedHash(password),
                    RequireChangeAtNextLogin = enforceChange
                });
            }

            var userInfo = user.ToInfoDTO();
            userInfo.PlainTextPassword = password;
            userInfos.Add(userInfo);
        }

        return userInfos;
    }

    public IEnumerable<User> VerifyPhonesAsync(List<User> users)
    {
        foreach (var user in users)
        {
            if (user.IsProtected)
                continue;

            if (string.IsNullOrWhiteSpace(user.Phone) || user.PhoneVerified)
                continue;

            if (authority is null)
                user.PhoneVerified = true;
            else
                RequireAdmission(new UserAccountChange { User = user, VerifyPhone = true });
        }

        return users;
    }

    public async Task<IEnumerable<(string Username, string Email, string Password)>> UserImportAsync(IEnumerable<UserImportUserDTO> userImports)
    {
        var reslut = new List<(string Username, string Email, string Password)>();

        var userImportsList = userImports.ToList();
        var usernames = userImportsList.Select(u => u.Username.ToLower()).ToList();
        var emails = userImportsList.Select(u => u.Email.ToLower()).ToList();

        var existingUsers = await db.Users
            .Where(u => !u.IsDeleted && (usernames.Contains(u.Username.ToLower()) || emails.Contains(u.Email.ToLower())))
            .Select(u => new { u.Username, u.Email })
            .ToListAsync();

        var existingUsernames = existingUsers.Select(u => u.Username.ToLower()).ToHashSet();
        var existingEmails = existingUsers.Select(u => u.Email.ToLower()).ToHashSet();

        var filteredUserImports = userImportsList
            .Where(u => !existingUsernames.Contains(u.Username.ToLower()) && !existingEmails.Contains(u.Email.ToLower()))
            .ToList();

        foreach (var userImport in filteredUserImports)
        {
            var password = PasswordGenerator.GeneratePassword(20);

            var userDto = new UserDTO
            {
                FullName = userImport.FullName,
                Username = userImport.Username,
                Phone = userImport.Phone,
                Email = userImport.Email,
                BirthDate = userImport.BirthDate,
                CompanyBranchID = new ShiftEntitySelectDTO { Value = userImport.CompanyBranchID },
                Password = password,
                IsActive = true,
                // Import keeps its previous behaviour: the configured default decides the forced change, and the
                // imported address is marked verified below, so no verification link is sent.
                RequireChangeAtNextLogin = configuration.Security.RequirePasswordChange,
                SendVerification = false,
            };

            // Routes through the base UpsertAsync → the User entity's IUpsertsShiftRepository hook → Base(). Base()
            // maps + audits + guards but does NOT dbSet.Add (only the CRUD handler does), so the explicit Add below
            // is still required for this direct call. With the staged authority the hook also queues the created row,
            // so its security state is recorded in the same save.
            var user = await UpsertAsync(new User(), userDto, ActionTypes.Insert, userId: null, idempotencyKey: null, disableDefaultDataLevelAccess: false, disableGlobalFilters: false);
            user.EmailVerified = true;

            if(!string.IsNullOrWhiteSpace(user.Phone))
                user.PhoneVerified = true;

            db.Users.Add(user);

            reslut.Add((userImport.Username, userImport.Email, password));
        }

        return reslut;
    }
}
