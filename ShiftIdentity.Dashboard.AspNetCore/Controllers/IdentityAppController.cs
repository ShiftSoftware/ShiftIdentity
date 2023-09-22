using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class IdentityAppController : ShiftEntitySecureControllerAsync<AppRepository, App, AppDTO, AppDTO>
    {
        public IdentityAppController(AppRepository repository) : base(ShiftIdentityActions.Apps)
        { }
    }
}
