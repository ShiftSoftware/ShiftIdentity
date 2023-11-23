
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Region : Profile
{
    public Region()
    {
        CreateMap<Core.Entities.Region, RegionDTO>().ReverseMap();
        CreateMap<Core.Entities.Region, RegionListDTO>();
        CreateMap<Core.Entities.Region, RegionModel>()
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => RegionContainerItemTypes.Region)
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
