using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityRegionController : ShiftEntitySecureControllerAsync<RegionRepository, Region, RegionListDTO, RegionDTO>
{
    public IdentityRegionController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.Regions,
        x =>
        {
            if(!dynamicActionFilters.DisableDefaultRegionFilter)
                x.FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.Regions)
                    .DecodeHashId<RegionDTO>()
                    .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
                    .IncludeSelfItems(ShiftEntity.Core.Constants.RegionIdClaim);

            x.DisableDefaultBrandFilter = dynamicActionFilters.DisableDefaultBrandFilter;
            x.DisableDefaultCityFilter = dynamicActionFilters.DisableDefaultCityFilter;
            x.DisableDefaultTeamFilter = dynamicActionFilters.DisableDefaultTeamFilter;
            x.DisableDefaultCountryFilter = dynamicActionFilters.DisableDefaultCountryFilter;
            x.DisableDefaultCompanyBranchFilter = dynamicActionFilters.DisableDefaultCompanyBranchFilter;
            x.DisableDefaultCompanyFilter = dynamicActionFilters.DisableDefaultCompanyFilter;
            x.DisableDefaultRegionFilter = dynamicActionFilters.DisableDefaultRegionFilter;
        }
    )
    { }
}