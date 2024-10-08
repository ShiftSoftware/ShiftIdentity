﻿using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.TypeAuth.Core.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCountryController : ShiftEntitySecureControllerAsync<CountryRepository, Country, CountryListDTO, CountryDTO>
{
    public IdentityCountryController() : base(ShiftIdentityActions.Countries,
        x => x
        .FilterBy(x => x.ID, ShiftIdentityActions.DataLevelAccess.Countries)
        .DecodeHashId<CountryDTO>()
        .IncludeCreatedByCurrentUser(x => x.CreatedByUserID)
        .IncludeSelfItems(ShiftEntity.Core.Constants.CountryIdClaim)
    )
    {
    }
}