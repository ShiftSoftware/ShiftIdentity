using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// THIN(ish) repository (Rung C): User's base CRUD is attribute-driven (the [ShiftEntitySecureEndpoint] on the User
// entity routes here). The repo SURVIVES because it's IUserRepository and holds the public methods the custom
// endpoints (UserEndpoints) + auth/account flows call. The heavy upsert moved to the User entity's
// IUpsertsShiftRepository hook; the mapper config lives in the base-ctor builder below. No UpsertAsync/DeleteAsync
// overrides — feature-lock + protected guard are central (Phase 0).
public class UserRepository :
    ShiftRepository<ShiftIdentityDbContext, User, UserListDTO, UserDTO>,
    IUserRepository
{
    private readonly ITypeAuthService typeAuthService;
    private readonly IMapper mapper;
    private readonly ShiftIdentityLocalizer Loc;

    public UserRepository(ShiftIdentityDbContext db,
        ITypeAuthService typeAuthService,
        IMapper mapper,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
        ShiftIdentityLocalizer Loc) : base(db, r =>
    {
        r.IncludeRelatedEntitiesWithFindAsync(
            x => x.Include(y => y.AccessTrees).ThenInclude(y => y.AccessTree),
            x => x.Include(y => y.UserLog),
            x => x.Include(y => y.TeamUsers),
            x => x.Include(y => y.CompanyBranch),
            x => x.Include(y => y.Company)
        );

        r.UseGeneratedMapper(map => map
            // ── VIEW ── (the FK convention can't fill CompanyBranchID: the DTO member is already named …ID, so the
            // convention appends another ID and misses — provide it explicitly, like the old profile map)
            .ForView(d => d.CompanyBranchID, e => new ShiftEntitySelectDTO { Value = e.CompanyBranchID.ToString()!, Text = e.CompanyBranch != null ? e.CompanyBranch.Name : null })
            .ForView(d => d.TotpEnabled, e => e.TotpSecret != null)
            .ForView(d => d.AccessTrees, e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name }).ToList())
            .IgnoreView(d => d.Password) // write-only; no entity source

            // ── ENTITY (write) ── Base() maps Username/IsActive/FullName/BirthDate; the hook owns Email/Phone/AccessTree/
            // password/CompanyBranch-derivation/UserAccessTrees, so those are Ignore'd (or ForEntity'd) here.
            .ForEntity(e => e.IntegrationId, dto => string.IsNullOrWhiteSpace(dto.IntegrationId) ? null : dto.IntegrationId)
            .IgnoreEntity(e => e.Email)
            .IgnoreEntity(e => e.Phone)
            .IgnoreEntity(e => e.AccessTree)

            // ── LIST ── flattened CompanyBranch name, the scope-ids CompanyBranchID/CompanyID (string←long?, must be
            // ForList — case matches but the list convention doesn't do long?→string; see the CompanyBranch note),
            // TotpEnabled, LastSeen (UserLog fallback), and the AccessTrees M:N projection.
            .ForList(d => d.CompanyBranch, e => e.CompanyBranch != null ? e.CompanyBranch.Name : null)
            .ForList(d => d.CompanyBranchID, e => e.CompanyBranchID.HasValue ? e.CompanyBranchID.Value.ToString() : null)
            .ForList(d => d.CompanyID, e => e.CompanyID.HasValue ? e.CompanyID.Value.ToString() : null)
            .ForList(d => d.TotpEnabled, e => e.TotpSecret != null)
            .ForList(d => d.LastSeen, e => ((e.UserLog == null || e.UserLog.LastSeen == null) ? e.LastSeen : e.UserLog.LastSeen) ?? default)
            .ForList(d => d.AccessTrees, e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name })));
    })
    {
        this.typeAuthService = typeAuthService;
        this.mapper = mapper;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
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

        if (!HashService.VerifyPassword(dto.CurrentPassword, user.Salt, user.PasswordHash))
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

    public async Task<User?> SetTotpSecret(byte[]? secret, User user)
    {
        user.TotpSecret = secret;

        return user;
    }

    public async Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId)
    {
        var user = await FindAsync(userId, null, disableGlobalFilters: true, disableDefaultDataLevelAccess: true);

        if (user is null)
            return null;

        //Check if the user is built-in
        if (user.IsProtected)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Username.ToLower() == dto.Username.ToLower() && x.ID != userId))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The username {0} exist", dto.Username]));

        //Check if the email is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Email.ToLower() == (dto.Email ?? "").ToLower() && x.ID != userId))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The email {0} exist", dto.Email]));

        //Assign phone
        string? formattedPhone = null;
        if (dto.Phone != null)
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));

            formattedPhone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);

            //Check if the phone is duplicate
            if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Phone.ToLower() == formattedPhone.ToLower() && x.ID != userId))
                throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The phone {0} exist", dto.Phone]));
        }

        //Assign values
        this.mapper.Map(dto, user);
        user.Phone = formattedPhone;

        return user;
    }

    public override Task<int> SaveChangesAsync()
    {
        return base.SaveChangesAsync();
    }

    public IEnumerable<UserInfoDTO> AssignRandomPasswords(List<User> users, int passwordLength, bool enforceChange)
    {
        var userInfos = new List<UserInfoDTO>();

        foreach (var user in users)
        {
            if (user.IsProtected)
                continue;

            var password = PasswordGenerator.GeneratePassword(passwordLength);

            var hash = HashService.GenerateHash(password);

            user.PasswordHash = hash.PasswordHash;
            user.Salt = hash.Salt;

            //Set flag to enforce password change
            user.RequireChangePassword = enforceChange;

            var userInfo = this.mapper.Map<UserInfoDTO>(user);
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

            if (!string.IsNullOrWhiteSpace(user.Phone))
                user.PhoneVerified = true;
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
            };

            // Routes through the base UpsertAsync → the User entity's IUpsertsShiftRepository hook → Base(). Base()
            // maps + audits + guards but does NOT dbSet.Add (only the CRUD handler does), so the explicit Add below
            // is still required for this direct call.
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
