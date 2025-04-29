using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class UserRepository :
    ShiftRepository<ShiftIdentityDbContext, User, UserListDTO, UserDTO>,
    IUserRepository
{

    private readonly ITypeAuthService typeAuthService;
    private readonly IMapper mapper;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;

    public UserRepository(ShiftIdentityDbContext db, 
        ITypeAuthService typeAuthService, 
        IMapper mapper,
        ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, 
        ShiftIdentityLocalizer Loc) : base(db, r =>
        r.IncludeRelatedEntitiesWithFindAsync(
            x => x.Include(y => y.AccessTrees).ThenInclude(y => y.AccessTree),
            x => x.Include(y => y.UserLog),
            x => x.Include(y => y.TeamUsers),
            x => x.Include(y => y.CompanyBranch)
        )
    )
    {
        this.typeAuthService = typeAuthService;
        this.mapper = mapper;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
    }

    public override async ValueTask<User> UpsertAsync(User entity, UserDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        if (shiftIdentityFeatureLocking.UserFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["User Feature is locked"]));

        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Username.ToLower() == dto.Username.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The username {0} exist", dto.Username]));

        //Check if the email is duplicate
        if (await db.Users.AnyAsync(x => !x.IsDeleted && x.Email.ToLower() == (dto.Email ?? "").ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The email {0} exist", dto.Email]));

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

        entity.Username = dto.Username;
        entity.IsActive = dto.IsActive;
        entity.Email = dto.Email;
        entity.FullName = dto.FullName;
        entity.BirthDate = dto.BirthDate;
        entity.Phone = formattedPhone;

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

            //Set flag to enforce password change
            entity.RequireChangePassword = true;
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

        return entity;
    }

    public override ValueTask<User> DeleteAsync(User entity, bool isHardDelete = false, long? userId = null)
    {
        if (shiftIdentityFeatureLocking.UserFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["User Feature is locked"]));

        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await db.Users.Include(x => x.UserLog).Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree)
            .Include(x => x.CompanyBranch)
            .FirstOrDefaultAsync(x => x.Username == username && !x.IsDeleted);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await db.Users.FirstOrDefaultAsync(x => x.Email == email && !x.IsDeleted);
    }

    public async Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId)
    {
        var user = await FindAsync(userId, null);
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

    public async Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId)
    {
        var user = await FindAsync(userId, null);

        if (user is null)
            return null;

        //Check if the user is built-in
        if (user.BuiltIn)
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

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }

    public IEnumerable<UserInfoDTO> AssignRandomPasswords(List<User> users)
    {
        var userInfos = new List<UserInfoDTO>();

        foreach (var user in users)
        {
            if (user.BuiltIn)
                continue;

            var password = PasswordGenerator.GeneratePassword(20);

            var hash = HashService.GenerateHash(password);

            user.PasswordHash = hash.PasswordHash;
            user.Salt = hash.Salt;

            //Set flag to enforce password change
            user.RequireChangePassword = true;

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
            if (user.BuiltIn)
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

            var user = await UpsertAsync(new User(), userDto, ActionTypes.Insert);
            user.EmailVerified = true;
            
            if(!string.IsNullOrWhiteSpace(user.Phone))
                user.PhoneVerified = true;

            db.Users.Add(user);

            reslut.Add((userImport.Username, userImport.Email, password));
        }

        return reslut;
    }
}
