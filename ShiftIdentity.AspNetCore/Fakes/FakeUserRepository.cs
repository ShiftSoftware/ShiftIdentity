﻿using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class FakeUserRepository : IUserRepository
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    public FakeUserRepository(ShiftIdentityOptions shiftIdentityOptions)
    {
        this.shiftIdentityOptions = shiftIdentityOptions;
    }
    public async Task<User> FindAsync(long id, DateTime? asOf = null)
    {
        return new User(id)
        {
            Email = shiftIdentityOptions.UserData?.Emails?.FirstOrDefault()?.Email ?? "",
            Phone = shiftIdentityOptions.UserData?.Phones?.FirstOrDefault()?.Phone ?? "",
            FullName = shiftIdentityOptions.UserData?.FullName ?? "",
            Username = shiftIdentityOptions.UserData?.Username ?? "",
            IsActive = true,
            AccessTrees = shiftIdentityOptions.AccessTrees.Select(x => new UserAccessTree
            {
                AccessTree = new AccessTree { Tree = x }
            }).ToList()
        };
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var hash = HashService.GenerateHash(shiftIdentityOptions.UserPassword!);

        return new User(this.shiftIdentityOptions.UserData.ID)
        {
            Email = shiftIdentityOptions.UserData?.Emails?.FirstOrDefault()?.Email ?? "",
            Phone = shiftIdentityOptions.UserData?.Phones?.FirstOrDefault()?.Phone ?? "",
            FullName = shiftIdentityOptions.UserData?.FullName ?? "",
            Username = shiftIdentityOptions.UserData?.Username ?? "",
            IsActive = true,
            PasswordHash = hash.PasswordHash,
            Salt = hash.Salt,
            AccessTrees = shiftIdentityOptions.AccessTrees.Select(x => new UserAccessTree
            {
                AccessTree = new AccessTree { Tree = x }
            }).ToList()
        };
    }

    public async Task SaveChangesAsync()
    {

    }

    public IQueryable<UserListDTO> OdataList(bool showDeletedRows = false)
    {
        return new List<UserListDTO> {
            new UserListDTO {
                ID = shiftIdentityOptions.UserData!.ID.ToString(),
                FullName = shiftIdentityOptions.UserData!.FullName,
            },
            new UserListDTO {
                ID = "2",
                FullName = "Second",
            },
        }.AsQueryable();
    }
}
