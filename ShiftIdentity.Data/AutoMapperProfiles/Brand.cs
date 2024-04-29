using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Brand : Profile
{
    public Brand()
    {
        CreateMap<Core.Entities.Brand, BrandDTO>().ReverseMap();
        CreateMap<Core.Entities.Brand, BrandListDTO>();
        CreateMap<Core.Entities.Brand, BrandModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
        CreateMap<Core.Entities.Brand, CompanyBranchSubItemModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Brand))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}