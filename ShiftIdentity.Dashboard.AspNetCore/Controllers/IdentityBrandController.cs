using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityBrandController : ShiftEntitySecureControllerAsync<BrandRepository, Brand, BrandListDTO, BrandDTO>
{
    public IdentityBrandController(DynamicActionFilters dynamicActionFilters) : base(ShiftIdentityActions.Brands)
    { }
}