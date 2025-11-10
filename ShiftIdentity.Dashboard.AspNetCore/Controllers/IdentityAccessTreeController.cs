using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class IdentityAccessTreeController : ShiftEntitySecureControllerAsync<AccessTreeRepository, AccessTree, AccessTreeListDTO, AccessTreeDTO>
    {
        public IdentityAccessTreeController(AccessTreeRepository repository) : base(ShiftIdentityActions.AccessTrees)
        { }
    }
}
