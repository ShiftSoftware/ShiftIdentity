using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class AccessTreeController : ShiftEntitySecureControllerAsync<AccessTreeRepository, AccessTree, AccessTreeDTO, AccessTreeDTO>
    {
        public AccessTreeController(AccessTreeRepository repository) : base(repository, ShiftIdentityActions.AccessTrees)
        { }
    }
}
