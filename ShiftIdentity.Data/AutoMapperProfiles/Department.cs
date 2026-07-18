
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// Entity↔DTO maps removed — "api/IdentityDepartment" is attribute-driven and uses the SOURCE-GENERATED mapper
// (see the Department entity). Only the Cosmos REPLICATION maps below remain.
public class Department : Profile
{
    public Department()
    {
        CreateMap<Data.Entities.Department, DepartmentModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
        CreateMap<Data.Entities.Department, CompanyBranchSubItemModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Department))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}
