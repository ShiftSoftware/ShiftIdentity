using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCompanyBranchController : ShiftEntitySecureControllerAsync<CompanyBranchRepository, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
{
    public IdentityCompanyBranchController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.CompanyBranches,
        x =>
        {
            //if (!dynamicActionFilters.DisableDefaultCompanyBranchFilter)
            //    x.FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.Branches)
            //        .DecodeHashId<CompanyBranchDTO>()
            //        .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
            //        .IncludeSelfItems(ShiftEntity.Core.Constants.CompanyBranchIdClaim);

            x.DisableDefaultBrandFilter = dynamicActionFilters.DisableDefaultBrandFilter;
            x.DisableDefaultCityFilter = dynamicActionFilters.DisableDefaultCityFilter;
            x.DisableDefaultTeamFilter = dynamicActionFilters.DisableDefaultTeamFilter;
            x.DisableDefaultCountryFilter = dynamicActionFilters.DisableDefaultCountryFilter;
            x.DisableDefaultCompanyBranchFilter = dynamicActionFilters.DisableDefaultCompanyBranchFilter;
            x.DisableDefaultCompanyFilter = dynamicActionFilters.DisableDefaultCompanyFilter;
            x.DisableDefaultRegionFilter = dynamicActionFilters.DisableDefaultRegionFilter;
        }
    )
    {

    }
}