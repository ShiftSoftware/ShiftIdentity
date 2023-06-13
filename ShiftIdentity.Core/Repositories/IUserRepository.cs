using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Repositories
{
    public interface IUserRepository
    {
        Task<User?> ChangePasswordAsync(ChangePasswordDTO dto, long userId);
        ValueTask<User> CreateAsync(UserDTO dto, long? userId = null);
        ValueTask<User> DeleteAsync(User entity, long? userId = null);
        Task<User> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false);
        string GetFormattedPhone(string phone);
        Task<User?> GetUserByUsernameAsync(string username);
        IQueryable<UserListDTO> OdataList(bool ignoreGlobalFilters = false);
        bool PhoneIsValid(string phone);
        Task SaveChangesAsync();
        ValueTask<User> UpdateAsync(User entity, UserDTO dto, long? userId = null);
        Task<User?> UpdateUserDataAsync(UserDataDTO dto, long userId);
        ValueTask<UserDTO> ViewAsync(User entity);
    }
}