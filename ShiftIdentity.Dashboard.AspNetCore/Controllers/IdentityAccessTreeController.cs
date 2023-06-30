using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class IdentityAccessTreeController : ShiftEntitySecureControllerAsync<AccessTreeRepository, AccessTree, AccessTreeDTO, AccessTreeDTO>
    {
        public IdentityAccessTreeController(AccessTreeRepository repository) : base(ShiftIdentityActions.AccessTrees)
        { }
    }
}
