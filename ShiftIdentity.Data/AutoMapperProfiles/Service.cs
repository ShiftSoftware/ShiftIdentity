
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Service : Profile
{
    public Service()
    {
        CreateMap<Core.Entities.Service, ServiceDTO>().ReverseMap();
        CreateMap<Core.Entities.Service, ServiceListDTO>();

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