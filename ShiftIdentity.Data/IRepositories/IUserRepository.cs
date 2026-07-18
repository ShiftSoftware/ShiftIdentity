using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.IRepositories;

public interface IUserRepository
{
    Task<User?> FindAsync(long id, DateTimeOffset? asOf = null, bool disableDefaultDataLevelAccess = false, bool disableGlobalFilters = false);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<int> SaveChangesAsync();
    ValueTask<IQueryable<UserListDTO>> OdataList(IQueryable<User>? queryable = null);
}