using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCompanyController : ShiftEntitySecureControllerAsync<CompanyRepository, Company, CompanyListDTO, CompanyDTO>
{
    public IdentityCompanyController() : base(ShiftIdentityActions.Companies)
    {
    }
}
