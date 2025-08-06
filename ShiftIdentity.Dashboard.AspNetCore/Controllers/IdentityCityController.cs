using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;


namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCityController : ShiftEntitySecureControllerAsync<CityRepository, City, CityListDTO, CityDTO>
{
    public IdentityCityController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.Cities,
        x =>
        {
            //if (!dynamicActionFilters.DisableDefaultCityFilter)
            //    x.FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.Cities)
            //        .DecodeHashId<CityDTO>()
            //        .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
            //        .IncludeSelfItems(ShiftEntity.Core.Constants.CityIdClaim);

            x.DisableDefaultBrandFilter = dynamicActionFilters.DisableDefaultBrandFilter;
            x.DisableDefaultCityFilter = dynamicActionFilters.DisableDefaultCityFilter;
            x.DisableDefaultTeamFilter = dynamicActionFilters.DisableDefaultTeamFilter;
            x.DisableDefaultCountryFilter = dynamicActionFilters.DisableDefaultCountryFilter;
            x.DisableDefaultCompanyBranchFilter = dynamicActionFilters.DisableDefaultCompanyBranchFilter;
            x.DisableDefaultCompanyFilter = dynamicActionFilters.DisableDefaultCompanyFilter;
            x.DisableDefaultRegionFilter = dynamicActionFilters.DisableDefaultRegionFilter;
        })
    { }
}