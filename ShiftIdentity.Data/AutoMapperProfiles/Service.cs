
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

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
        CreateMap<Core.Entities.Service, CompanyBranchServiceModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyItemTypes.Service))
            .ForMember(dest => dest.BranchID, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyID, opt => opt.Ignore())
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}
