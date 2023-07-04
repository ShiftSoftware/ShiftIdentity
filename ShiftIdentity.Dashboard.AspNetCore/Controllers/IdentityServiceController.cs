﻿using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityServiceController : ShiftEntitySecureControllerAsync<ServiceRepository, Service, ServiceListDTO, ServiceDTO>
{
    public IdentityServiceController() : base(ShiftIdentityActions.Services)
    { }
}