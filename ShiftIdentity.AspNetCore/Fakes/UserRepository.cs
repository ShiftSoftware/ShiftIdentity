using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Repositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class UserRepository : IUserRepository
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    public UserRepository(ShiftIdentityOptions shiftIdentityOptions)
    {
        this.shiftIdentityOptions = shiftIdentityOptions;
    }

    public Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId)
    {
        throw new NotImplementedException();
    }

    public ValueTask<User> CreateAsync(UserDTO dto, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask<User> DeleteAsync(User entity, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public Task<User> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
    {
        throw new NotImplementedException();
    }

    public string GetFormattedPhone(string phone)
    {
        throw new NotImplementedException();
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var hash = HashService.GenerateHash("one");

        return new User
        {
            Email = "",
            Phone = "",
            FullName = "Fake User",
            Username = "fake-user",
            IsActive = true,
            PasswordHash = hash.PasswordHash,
            Salt = hash.Salt,
            AccessTrees = shiftIdentityOptions.AccessTrees.Select(x => new UserAccessTree
            {
                AccessTree = new AccessTree { Tree = x }
            }).ToList()
        };
    }

    public IQueryable<UserListDTO> OdataList(bool ignoreGlobalFilters = false)
    {
        throw new NotImplementedException();
    }

    public bool PhoneIsValid(string phone)
    {
        throw new NotImplementedException();
    }

    public async Task SaveChangesAsync()
    {

    }

    public ValueTask<User> UpdateAsync(User entity, UserDTO dto, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId)
    {
        throw new NotImplementedException();
    }

    public ValueTask<UserDTO> ViewAsync(User entity)
    {
        throw new NotImplementedException();
    }
}
