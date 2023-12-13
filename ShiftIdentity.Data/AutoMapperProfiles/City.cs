using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;


namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class City : Profile
{
    public City()
    {
        CreateMap<Core.Entities.City, CityDTO>()
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.Region.ID.ToString()!, Text = src.Region!.Name })
                )
            .ReverseMap()
            .ForMember(x => x.Region, x => x.Ignore())
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.Region.Value.ToLong())
            );

        CreateMap<Core.Entities.City, CityListDTO>()
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => src.Region.Name)
                );

        CreateMap<Core.Entities.City, CityModel>()
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.RegionID.ToString())
            )
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => RegionContainerItemTypes.City)
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );

        CreateMap<Core.Entities.City, CityCompanyBranchModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
