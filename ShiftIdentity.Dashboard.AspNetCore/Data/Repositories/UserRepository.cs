﻿using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.TypeAuth.AspNetCore.Services;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class UserRepository :
    ShiftRepository<ShiftIdentityDB, User, UserListDTO, UserDTO, UserDTO>,
    IShiftRepositoryAsync<User, UserListDTO, UserDTO>,
    IUserRepository
{

    private readonly TypeAuthService typeAuthService;
    public UserRepository(ShiftIdentityDB db, TypeAuthService typeAuthService, IMapper mapper) : base(db, db.Users, mapper)
    {
        this.typeAuthService = typeAuthService;
    }

    public override async ValueTask<User> UpsertAsync(User entity, UserDTO dto, ActionTypes actionType, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the username is duplicate
        if (await db.Users.AnyAsync(x => x.Username.ToLower() == dto.Username.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message("Duplicate", $"the username {dto.Username} is exists"));

        if (actionType == ActionTypes.Insert)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ShiftEntityException(new Message("Validation Error", "Password can not be empty."));
        }

        entity.Username = dto.Username;
        entity.IsActive = dto.IsActive;
        entity.Email = dto.Email;
        entity.FullName = dto.FullName;
        entity.BirthDate = dto.BirthDate;

        if (dto.CompanyBranchID != null)
        {
            entity.CompanyBranchID = dto.CompanyBranchID.Value.ToLong();

            var companyBranch = await db.CompanyBranches.FindAsync(entity.CompanyBranchID.Value);

            entity.RegionID = companyBranch!.RegionID;

            entity.CompanyID = companyBranch.CompanyID;
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

        entity.AccessTree = typeAuth_Producer.GenerateAccessTree(typeAuthService, typeAuth_Preserver);

        if (dto.Phone != null)
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

            entity.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
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

            var inAccessibleActions = this.typeAuthService.FindInAccessibleActionsOn(tAuthForThisTree);

            if (inAccessibleActions.Count > 0)
            {
                inaccessibleAccessTress[tree] = inAccessibleActions;
            }
        }

        if (inaccessibleAccessTress.Count > 0)
        {
            throw new ShiftEntityException(new Message(
                "Error",
                "Below Access Trees contain accesses that you can not grant",
                inaccessibleAccessTress.Select(x => new Message(x.Key.Name, null!, x.Value.Select(y => new Message(y.Key.Name!, y.Value.ToString()!)).ToList())).ToList()
            ));
        }

        foreach (var item in entity.AccessTrees)
        {
            db.UserAccessTrees.Remove(item);
        }

        entity.AccessTrees = dto.AccessTrees.Select(x => new UserAccessTree
        {
            AccessTree = trees[x.Value.ToLong()]
        }).ToList();

        return entity;
    }

    public override ValueTask<User> DeleteAsync(User entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await db.Users.Include(x => x.AccessTrees).ThenInclude(x => x.AccessTree).FirstOrDefaultAsync(x => x.Username == username);
    }

    public async Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId)
    {
        var user = await FindAsync(userId, null);
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
        var user = await FindAsync(userId, null);

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
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

            user.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
        }

        return user;
    }

    public override Task SaveChangesAsync()
    {
        return base.SaveChangesAsync();
    }
}
