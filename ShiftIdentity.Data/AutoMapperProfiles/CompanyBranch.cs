using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (CompanyBranch↔CompanyBranchDTO, CompanyBranch→CompanyBranchListDTO) are gone: the
// "api/IdentityCompanyBranch" endpoint is attribute-driven and routes through the thin CompanyBranchRepository,
// whose base-ctor builder opts into the SOURCE-GENERATED mapper (ForView lat/long/CustomFields/M:N, ForEntity
// lat/long, ForList flattened names/display-orders/scope-ids/M:N). Write logic moved to the CompanyBranch entity's
// IUpsertsShiftRepository hook. Only the Cosmos REPLICATION map below remains — no generated equivalent.
public class CompanyBranch : Profile
{
    public CompanyBranch()
    {
        CreateMap<Data.Entities.CompanyBranch, CompanyBranchModel>()
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.ID))
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Branch))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.Location,
                opt => opt.MapFrom(src => (string.IsNullOrWhiteSpace(src.Longitude) || string.IsNullOrWhiteSpace(src.Latitude)) ? null : new Location(new decimal[] { decimal.Parse(src.Longitude), decimal.Parse(src.Latitude) }))
            );
    }
}
