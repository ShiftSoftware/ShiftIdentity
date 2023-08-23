using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data;
using ShiftSoftware.TypeAuth.AspNetCore.Services;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class UserRepository :
    ShiftRepository<ShiftIdentityDB, User>,
    IShiftRepositoryAsync<User, UserListDTO, UserDTO>,
    IUserRepository
{

    private readonly ShiftIdentityDB db;
    private readonly TypeAuthService typeAuthService;
    public UserRepository(ShiftIdentityDB db, TypeAuthService typeAuthService, IMapper mapper) : base(db, db.Users, mapper)
    {
        this.db = db;
        this.typeAuthService = typeAuthService;
    }
    public async ValueTask<User> CreateAsync(UserDTO dto, long? userId = null)
    {
        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => x.Username.ToLower() == dto.Username.ToLower()))
            throw new ShiftEntityException(new Message("Duplicate", $"the username {dto.Username} is exists"));

        var entity = new User().CreateShiftEntity(userId);

        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new ShiftEntityException(new Message("Validation Error", "Password can not be empty."));

        await AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<User> DeleteAsync(User entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<User>(entity);
    }

    public async Task<User> FindAsync(long id, DateTime? asOf = null)
    {
        return await base.FindAsync(id, asOf, x => x.Include(y => y.AccessTrees).ThenInclude(y => y.AccessTree));
    }

    public IQueryable<UserListDTO> OdataList(bool showDeletedRows = false)
    {
        IQueryable<User> users = GetIQueryable(showDeletedRows).AsNoTracking();

        return mapper.ProjectTo<UserListDTO>(users);
    }

    public async ValueTask<User> UpdateAsync(User entity, UserDTO dto, long? userId = null)
    {
        //Check if the username is duplicate
        if (dto.Username.ToLower() != entity.Username.ToLower())
            if (await db.Users.AnyAsync(x => x.Username.ToLower() == dto.Username.ToLower()))
                throw new ShiftEntityException(new Message("Duplicate", $"the username {dto.Username} is exists"));

        entity.UpdateShiftEntity(userId);

        await AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<UserDTO> ViewAsync(User entity)
    {
        return new ValueTask<UserDTO>(mapper.Map<UserDTO>(entity));
    }

    private async Task AssignValues(UserDTO dto, User entity)
    {
        entity.Username = dto.Username;
        entity.IsActive = dto.IsActive;
        entity.Email = dto.Email;
        entity.FullName = dto.FullName;
        entity.BirthDate = dto.BirthDate;

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

        entity.AccessTree = typeAuth_Producer.GenerateAccessTree(typeAuthService, typeAuth_Preserver);

        if (dto.Phone != null)
        {
            if (!PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

            entity.Phone = GetFormattedPhone(dto.Phone);
        }

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

        foreach (var item in entity.AccessTrees)
        {
            db.UserAccessTrees.Remove(item);
        }

        entity.AccessTrees = dto.AccessTrees.Select(x => new UserAccessTree
        {
            AccessTree = trees[x.Value.ToLong()]
        }).ToList();

        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);
    }

    public static string GetFormattedPhone(string phone)
    {
        var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

        var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

        return phoneNumberUtil.Format(phoneNumber, PhoneNumbers.PhoneNumberFormat.INTERNATIONAL);
    }

    public static bool PhoneIsValid(string phone)
    {
        var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();

        var phoneNumber = phoneNumberUtil.Parse(phone, "IQ");

        return phoneNumberUtil.IsValidNumber(phoneNumber);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await db.Users.Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree).FirstOrDefaultAsync(x => x.Username == username);
    }

    public async Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId)
    {
        var user = await FindAsync(userId);
        if (user is null)
            return null;

        if (!HashService.VerifyPassword(dto.CurrentPassword, user.Salt, user.PasswordHash))
            return null;

        user.UpdateShiftEntity(userId);
        var hash = HashService.GenerateHash(dto.NewPassword);
        user.PasswordHash = hash.PasswordHash;
        user.Salt = hash.Salt;

        //Set enforce password change flag to false
        user.RequireChangePassword = false;

        return user;
    }

    public async Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId)
    {
        var user = await FindAsync(userId);

        if (user is null)
            return null;

        user.UpdateShiftEntity(userId);

        //Assign values
        user.Username = dto.Username;
        user.Email = dto.Email;
        user.FullName = dto.FullName;
        user.BirthDate = dto.BirthDate;

        //Assign phone
        if (dto.Phone != null)
        {
            if (!PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

            user.Phone = GetFormattedPhone(dto.Phone);
        }

        return user;
    }

    public override Task SaveChangesAsync()
    {
        return base.SaveChangesAsync();
    }
}
