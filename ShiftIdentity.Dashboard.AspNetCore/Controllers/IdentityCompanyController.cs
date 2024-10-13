using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCompanyController : ShiftEntitySecureControllerAsync<CompanyRepository, Company, CompanyListDTO, CompanyDTO>
{
    public IdentityCompanyController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.Companies,
        x =>
        {
            if (!dynamicActionFilters.DisableDefaultCompanyFilter)
                x.FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.Companies)
                    .DecodeHashId<CompanyDTO>()
                    .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
                    .IncludeSelfItems(ShiftEntity.Core.Constants.CompanyIdClaim);

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