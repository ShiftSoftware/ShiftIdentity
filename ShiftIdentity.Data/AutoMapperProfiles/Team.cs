using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (Team↔TeamDTO, Team→TeamListDTO) are gone: the "api/IdentityTeam" endpoint is
// attribute-driven and uses the SOURCE-GENERATED mapper (see Team entity — its IConfiguresShiftRepository
// reproduces the M:N Users/CompanyBranches projections, Tags, and the flattened Company name). Only the Cosmos
// REPLICATION maps below remain — they have no generated equivalent and drive the replication pipeline.
public class Team : Profile
{
    public Team()
    {
        CreateMap<Data.Entities.Team, TeamModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(x => x.CompanyBranches, x => x.MapFrom(x => x.TeamCompanyBranches));

        CreateMap<TeamCompanyBranch, CompanyBranchSubItemModel>()
            .ForMember(x => x.Name, x => x.MapFrom(x => x.CompanyBranch.Name))
            .ForMember(x => x.IntegrationId, x => x.MapFrom(x => x.CompanyBranch.IntegrationId))
            .ForMember(x => x.BranchID, x => x.MapFrom(x => x.CompanyBranch.ID))
            .ForMember(x => x.ItemType, x => x.MapFrom(x => CompanyBranchContainerItemTypes.Branch));
    }
}
