using Microsoft.AspNetCore.Mvc;
using ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class UserController : ShiftEntitySecureControllerAsync<UserRepository, User, UserListDTO, UserDTO>
    {
        public UserController(UserRepository repository) : base(repository, ShiftIdentityActions.Users)
        { }
    }
}
