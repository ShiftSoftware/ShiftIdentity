using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityDepartmentController : ShiftEntitySecureControllerAsync<DepartmentRepository, Department, DepartmentListDTO, DepartmentDTO>
{
    public IdentityDepartmentController() : base(ShiftIdentityActions.Departments)
    { }
}
