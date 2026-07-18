using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (Company↔CompanyDTO, Company→CompanyListDTO) are gone: the "api/IdentityCompany" endpoint is
// attribute-driven and routes through the thin CompanyRepository, whose base-ctor builder opts into the
// SOURCE-GENERATED mapper (ForView CustomFields/ParentCompany, ForList ParentCompanyName/ParentCompanyID/Brands).
// The CustomFields write-merge moved to the Company entity's IUpsertsShiftRepository hook. Only the Cosmos
// REPLICATION map below remains — it has no generated equivalent and drives the replication pipeline.
public class Company : Profile
{
    public Company()
    {
        CreateMap<Data.Entities.Company, CompanyModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
