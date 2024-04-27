using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityUserGroupController : ShiftEntitySecureControllerAsync<UserGroupRepository, UserGroup, UserGroupListDTO, UserGroupDTO>
{
    public IdentityUserGroupController() : base(ShiftIdentityActions.UserGroups,
        x => x
        .FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.UserGroups)
        .DecodeHashId<UserGroupDTO>()
        .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
        .IncludeSelfItems(ShiftEntity.Core.Constants.UserGroupIdsClaim)
    )
    {
    }
}