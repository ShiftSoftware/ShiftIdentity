
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// Entity↔DTO maps removed — "api/IdentityService" is attribute-driven and uses the SOURCE-GENERATED mapper
// (see the Service entity). Only the Cosmos REPLICATION maps below remain.
public class Service : Profile
{
    public Service()
    {
        CreateMap<Core.Entities.Service, ServiceModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
        CreateMap<Core.Entities.Service, CompanyBranchSubItemModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Service))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}