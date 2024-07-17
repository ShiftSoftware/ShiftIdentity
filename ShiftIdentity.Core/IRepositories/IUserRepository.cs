using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.IRepositories;

public interface IUserRepository
{
    Task<User?> FindAsync(long id, DateTimeOffset? asOf = null);
    Task<User?> GetUserByUsernameAsync(string username);
    Task SaveChangesAsync(bool wrapInTransaction = false);
    IQueryable<UserListDTO> OdataList(bool showDeletedRows = false, IQueryable<User>? queryable = null);
}