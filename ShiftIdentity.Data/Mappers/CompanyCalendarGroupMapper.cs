using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ShiftMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Mappers;

/// <summary>
/// The company calendar's JSON children, customized ONCE for every parent that nests them. A calendar's
/// ShiftGroups and WeekendGroups nest automatically from the DTO graph (view and write); what convention cannot
/// do is their <c>Departments</c>/<c>Brands</c>, which are hashid-ENCODED select lists over plain <c>long</c> id
/// lists on the entity side — so the four child pairs are declared here with that one customization each, and
/// ShiftMapper uses them wherever the parent map reaches them.
/// <para>
/// An ordinary mapper class: not partial, nothing injects it. The hash-id service is read from
/// <see cref="ShiftMapperBase.Services"/> WHEN A GROUP IS MAPPED rather than injected into the constructor, on
/// purpose: a generated mapper constructs every class it composes the first time anything is mapped, so a
/// constructor dependency would make the whole identity mapper unusable outside a container —
/// <c>Mapper.Create(assembly)</c>, which the replication goldens and any host without DI rely on. Read this way,
/// only a map of a calendar group needs the container.
/// </para>
/// </summary>
public class CompanyCalendarGroupMapper : ShiftMapperBase
{
    public CompanyCalendarGroupMapper()
    {
        var hashIds = new Lazy<IHashIdService>(() => Services.GetRequiredService<IHashIdService>());

        CreateMap<CompanyCalendarShiftGroup, CompanyCalendarShiftGroupDTO>()
            .ForMember(d => d.Departments, opt => opt.MapFrom(sg => sg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(d => d.Brands, opt => opt.MapFrom(sg => sg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<BrandListDTO>(id) }).ToList()));

        CreateMap<CompanyCalendarShiftGroupDTO, CompanyCalendarShiftGroup>()
            .ForMember(sg => sg.DepartmentIds, opt => opt.MapFrom(d => d.Departments.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<DepartmentListDTO>(x.Value!)).ToList()))
            .ForMember(sg => sg.BrandIds, opt => opt.MapFrom(d => d.Brands.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<BrandListDTO>(x.Value!)).ToList()));

        CreateMap<CompanyCalendarWeekendGroup, CompanyCalendarWeekendGroupDTO>()
            .ForMember(d => d.Departments, opt => opt.MapFrom(wg => wg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(d => d.Brands, opt => opt.MapFrom(wg => wg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<BrandListDTO>(id) }).ToList()));

        CreateMap<CompanyCalendarWeekendGroupDTO, CompanyCalendarWeekendGroup>()
            .ForMember(wg => wg.DepartmentIds, opt => opt.MapFrom(d => d.Departments.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<DepartmentListDTO>(x.Value!)).ToList()))
            .ForMember(wg => wg.BrandIds, opt => opt.MapFrom(d => d.Brands.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<BrandListDTO>(x.Value!)).ToList()));
    }
}
