using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCountryController : ShiftEntitySecureControllerAsync<CountryRepository, Country, CountryListDTO, CountryDTO>
{
    public IdentityCountryController() : base(ShiftIdentityActions.Countries)
    {
    }
}