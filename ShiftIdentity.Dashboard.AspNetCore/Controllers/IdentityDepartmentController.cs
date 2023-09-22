using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityDepartmentController : ShiftEntitySecureControllerAsync<DepartmentRepository, Department, DepartmentListDTO, DepartmentDTO>
{
    public IdentityDepartmentController() : base(ShiftIdentityActions.Departments)
    { }
}
