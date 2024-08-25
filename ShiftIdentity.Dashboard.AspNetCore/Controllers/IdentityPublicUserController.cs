using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Web.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class IdentityPublicUserController : ControllerBase
    {
        IUserRepository userRepository;
        public IdentityPublicUserController(IUserRepository userRepository)
        {
            this.userRepository = userRepository;
        }

        //[EnableQueryWithHashIdConverter]
        public async Task<ActionResult<ODataDTO<PublicUserListDTO>>> Get(ODataQueryOptions<PublicUserListDTO> oDataQueryOptions)
        {
            var data = userRepository.OdataList().Select(x => new PublicUserListDTO { ID = x.ID, Name = x.FullName });

            return Ok(await ODataIqueryable.GetOdataDTOFromIQueryable(data, oDataQueryOptions, Request));
        }
    }
}
