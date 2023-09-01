using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class IdentityUserController : ShiftEntitySecureControllerAsync<UserRepository, User, UserListDTO, UserDTO>
    {
        public IdentityUserController(UserRepository repository) : base(ShiftIdentityActions.Users)
        { }
    }
}
