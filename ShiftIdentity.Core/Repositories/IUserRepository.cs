using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Repositories;

public interface IUserRepository
{
    Task<User> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false);
    Task<User?> GetUserByUsernameAsync(string username);
    Task SaveChangesAsync();
    IQueryable<UserListDTO> OdataList(bool ignoreGlobalFilters = false);
}