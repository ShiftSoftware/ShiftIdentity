using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Authorize]
    public class IdentityPublicUserController : ControllerBase
    {
        IUserRepository userRepository;
        public IdentityPublicUserController(IUserRepository userRepository)
        {
            this.userRepository = userRepository;
        }

        [EnableQueryWithHashIdConverter]
        public IActionResult Get([FromQuery] bool ignoreGlobalFilters = false)
        {
            return Ok(userRepository.OdataList(ignoreGlobalFilters).Select(x => new PublicUserListDTO { ID = x.ID, Name = x.FullName }));
        }
    }
}
