using Microsoft.AspNetCore.Mvc;
using ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class AccessTreeController : ShiftEntitySecureControllerAsync<AccessTreeRepository, AccessTree, AccessTreeDTO, AccessTreeDTO>
    {
        public AccessTreeController(AccessTreeRepository repository) : base(repository, ShiftIdentityActions.AccessTrees)
        { }
    }
}
