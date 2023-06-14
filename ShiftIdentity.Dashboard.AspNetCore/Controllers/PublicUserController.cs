using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftEntity.Web.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Authorize]
    public class PublicUserController : ControllerBase
    {
        UserRepository userRepository;
        public PublicUserController(UserRepository userRepository)
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
