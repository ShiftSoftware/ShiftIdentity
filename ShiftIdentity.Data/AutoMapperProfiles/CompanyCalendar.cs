using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class CompanyCalendar : Profile
{
    public CompanyCalendar()
    {
        // ShiftGroup JSON child types
        CreateMap<CompanyCalendarShiftGroup, CompanyCalendarShiftGroupDTO>()
            .ForMember(x => x.Departments, x => x.MapFrom(s => s.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = ShiftEntityHashIdService.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(x => x.Brands, x => x.MapFrom(s => s.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = ShiftEntityHashIdService.Encode<BrandListDTO>(id) }).ToList()))
        .ReverseMap()
            .ForMember(x => x.DepartmentIds, x => x.MapFrom(s => s.Departments.Where(d => d.Value != null).Select(d => ShiftEntityHashIdService.Decode<DepartmentListDTO>(d.Value!)).ToList()))
            .ForMember(x => x.BrandIds, x => x.MapFrom(s => s.Brands.Where(b => b.Value != null).Select(b => ShiftEntityHashIdService.Decode<BrandListDTO>(b.Value!)).ToList()));
        CreateMap<CompanyCalendarShift, CompanyCalendarShiftItemDTO>().ReverseMap();

        // WeekendGroup JSON child types
        CreateMap<CompanyCalendarWeekendGroup, CompanyCalendarWeekendGroupDTO>()
            .ForMember(x => x.Departments, x => x.MapFrom(s => s.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = ShiftEntityHashIdService.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(x => x.Brands, x => x.MapFrom(s => s.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = ShiftEntityHashIdService.Encode<BrandListDTO>(id) }).ToList()))
        .ReverseMap()
            .ForMember(x => x.DepartmentIds, x => x.MapFrom(s => s.Departments.Where(d => d.Value != null).Select(d => ShiftEntityHashIdService.Decode<DepartmentListDTO>(d.Value!)).ToList()))
            .ForMember(x => x.BrandIds, x => x.MapFrom(s => s.Brands.Where(b => b.Value != null).Select(b => ShiftEntityHashIdService.Decode<BrandListDTO>(b.Value!)).ToList()));
        CreateMap<CompanyCalendarWeekendRule, CompanyCalendarWeekendRuleItemDTO>().ReverseMap();

        // Entity <-> ListDTO
        CreateMap<Core.Entities.CompanyCalendar, CompanyCalendarListDTO>()
            .ForMember(x => x.CompanyID, x => x.MapFrom(s => s.CompanyID.HasValue ? s.CompanyID.ToString()! : null));

        // Entity <-> DTO
        CreateMap<Core.Entities.CompanyCalendar, CompanyCalendarDTO>()
            .ForMember(x => x.Company, x => x.MapFrom(s => s.CompanyID.HasValue ? new ShiftEntitySelectDTO { Value = s.CompanyID.ToString()! } : null))
            .ForMember(x => x.Branches, x => x.MapFrom(s => s.Branches.Select(b => new ShiftEntitySelectDTO { Value = b.CompanyBranchID.ToString() }).ToList()))
        .ReverseMap()
            .ForMember(x => x.CompanyID, x => x.MapFrom(s => s.Company != null ? s.Company.Value : null))
            .ForMember(x => x.Branches, x => x.Ignore())
            .AfterMap((src, dest, ctx) =>
            {
                dest.Branches ??= new HashSet<CompanyCalendarBranch>();

                var branchesToRemove = dest.Branches
                    .Where(b => !src.Branches.Any(s => s.Value == b.CompanyBranchID.ToString()))
                    .ToList();
                foreach (var b in branchesToRemove)
                    dest.Branches.Remove(b);

                foreach (var branchDto in src.Branches)
                {
                    if (!dest.Branches.Any(b => b.CompanyBranchID.ToString() == branchDto.Value))
                        dest.Branches.Add(new CompanyCalendarBranch { CompanyBranchID = branchDto.Value.ToLong() });
                }
            });
    }
}
