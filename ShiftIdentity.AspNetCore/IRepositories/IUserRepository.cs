using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;

public interface IUserRepository
{
    Task<User> FindAsync(long id, DateTime? asOf = null, System.Linq.Expressions.Expression<Func<User, bool>>? where = null);
    Task<User?> GetUserByUsernameAsync(string username);
    Task SaveChangesAsync(bool wrapInTransaction = false);
    IQueryable<UserListDTO> OdataList(bool showDeletedRows = false, IQueryable<User>? queryable = null);
}