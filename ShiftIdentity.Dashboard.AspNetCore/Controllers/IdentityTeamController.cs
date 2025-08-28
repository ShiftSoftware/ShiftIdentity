using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityTeamController : ShiftEntitySecureControllerAsync<TeamRepository, Team, TeamListDTO, TeamDTO>
{
    public IdentityTeamController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.Teams)
    {
    }
}