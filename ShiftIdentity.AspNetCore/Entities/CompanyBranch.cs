using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
public class CompanyBranch : ShiftEntity<CompanyBranch>
{
    public string Name { get; set; } = default!;
    public long CompanyID { get; set; }
    public long RegionID { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public virtual Company Company { get; set; } = default!;
    public virtual Region Region { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment> CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService> CompanyBranchServices { get; set; }

    public CompanyBranch()
    {
        this.CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        this.CompanyBranchServices = new HashSet<CompanyBranchService>();
    }

    public static implicit operator CompanyBranchListDTO(CompanyBranch entity)
    {
        if (entity == null)
            return default!;

        return new CompanyBranchListDTO
        {
            ID = entity.ID.ToString(),
            Name = entity.Name,
            ExternalId = entity.ExternalId,
            ShortCode = entity.ShortCode,
            Company = entity.Company.Name,
            Region = entity.Region.Name,
            Departments = entity.CompanyBranchDepartments.Select(y => new ShiftEntity.Model.Dtos.ShiftEntitySelectDTO
            {
                Value = y.DepartmentID.ToString(),
                Text = y.Department.Name,
            })
        };
    }

    public static implicit operator CompanyBranchDTO(CompanyBranch entity)
    {
        if (entity == null)
            return default!;

        return new CompanyBranchDTO
        {
            ID = entity.ID.ToString(),
            CreateDate = entity.CreateDate,
            CreatedByUserID = entity.CreatedByUserID.ToString(),
            IsDeleted = entity.IsDeleted,
            LastSaveDate = entity.LastSaveDate,
            LastSavedByUserID = entity.LastSavedByUserID.ToString(),

            Name = entity.Name,
            ShortCode = entity.ShortCode,
            ExternalId = entity.ExternalId,
            Address = entity.Address,
            Email = entity.Email,
            Phone = entity.Phone,

            Company = new ShiftEntity.Model.Dtos.ShiftEntitySelectDTO
            {
                Value = entity.CompanyID.ToString(),
                Text = entity.Company.Name
            },
            Region = new ShiftEntity.Model.Dtos.ShiftEntitySelectDTO
            {
                Value = entity.RegionID.ToString(),
                Text = entity.Region.Name
            },
            Departments = entity.CompanyBranchDepartments.Select(y => new ShiftEntity.Model.Dtos.ShiftEntitySelectDTO
            {
                Value = y.DepartmentID.ToString(),
                Text = y.Department.Name,
            }),
            Services = entity.CompanyBranchServices.Select(y => new ShiftEntity.Model.Dtos.ShiftEntitySelectDTO
            {
                Value = y.ServiceID.ToString(),
                Text = y.Service.Name,
            })
        };
    }
}
