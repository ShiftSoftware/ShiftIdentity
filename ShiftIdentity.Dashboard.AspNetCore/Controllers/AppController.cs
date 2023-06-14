using Microsoft.AspNetCore.Mvc;
using ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftIdentity.Dashboard.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    public class AppController : ShiftEntitySecureControllerAsync<AppRepository, App, AppDTO, AppDTO>
    {
        public AppController(AppRepository repository) : base(repository, ShiftIdentityActions.Apps)
        { }
    }
}
