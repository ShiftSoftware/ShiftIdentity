using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class FakeUserRepository : IUserRepository
{
    private readonly ShiftIdentityOptions shiftIdentityOptions;
    public FakeUserRepository(ShiftIdentityOptions shiftIdentityOptions)
    {
        this.shiftIdentityOptions = shiftIdentityOptions;
    }
    public async Task<User?> FindAsync(long id, DateTimeOffset? asOf = null, bool disableDefaultDataLevelAccess = false, bool disableGlobalFilters = false)
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
            }).ToList(),
            RegionID = 1,
            CompanyBranchID = 1,
            CompanyID = 1
        };
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var hash = HashService.GenerateHash(shiftIdentityOptions.UserPassword!);

        return new User(this.shiftIdentityOptions.UserData.ID.ToLong())
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

    public ValueTask<IQueryable<UserListDTO>> OdataList(IQueryable<User>? queryable = null)
    {
        return new ValueTask<IQueryable<UserListDTO>>(new List<UserListDTO> {
            new UserListDTO {
                ID = shiftIdentityOptions.UserData!.ID.ToString(),
                FullName = shiftIdentityOptions.UserData!.FullName,
            },
            new UserListDTO {
                ID = "2",
                FullName = "Second",
            },
        }.AsQueryable());
    }
}
