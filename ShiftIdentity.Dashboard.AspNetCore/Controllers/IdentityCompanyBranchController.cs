﻿using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCompanyBranchController : ShiftEntitySecureControllerAsync<CompanyBranchRepository, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
{
    public IdentityCompanyBranchController() : base(ShiftIdentityActions.CompanyBranches)
    {

    }
}
