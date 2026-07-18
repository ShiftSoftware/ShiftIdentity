using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (Brand↔BrandDTO, Brand→BrandListDTO) are gone: the "api/IdentityBrand" endpoint is
// attribute-driven and uses the SOURCE-GENERATED mapper (see Brand entity). Only the Cosmos REPLICATION maps
// below remain — they have no generated equivalent and are still used by the replication pipeline.
public class Brand : Profile
{
    public Brand()
    {
        CreateMap<Data.Entities.Brand, BrandModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
        CreateMap<Data.Entities.Brand, CompanyBranchSubItemModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Brand))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}