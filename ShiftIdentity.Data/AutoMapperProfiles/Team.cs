using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Team : Profile
{
    public Team()
    {
        CreateMap<Core.Entities.Team, TeamDTO>()
            .ForMember(
                    dest => dest.Company,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO (src.CompanyID.ToString()!, src.Company!.Name ))
                )
            .ForMember(x => x.Users, opt => opt.MapFrom(x => x.TeamUsers.Select(y => new ShiftEntitySelectDTO (y.UserID.ToString()!, y.User.Username ))))
            .ForMember(x => x.CompanyBranches, opt => opt.MapFrom(x => x.TeamCompanyBranches.Select(y => new ShiftEntitySelectDTO (y.CompanyBranchID.ToString()!, y.CompanyBranch.Name ))))
            .ReverseMap()
            .ForMember(x => x.TeamUsers, m => m.MapFrom(x => x.Users.Select(s =>
                new TeamUser
                {
                    UserID = s.Value.ToLong()
                }
            )))
            .ForMember(x => x.TeamCompanyBranches, m => m.MapFrom(x => x.CompanyBranches.Select(s =>
                new TeamCompanyBranch
                {
                    CompanyBranchID = s.Value.ToLong()
                }
            )));

        CreateMap<Core.Entities.Team, TeamModel>()
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

        CreateMap<Core.Entities.Team, TeamListDTO>()
            .ForMember(
                dest => dest.Company,
                opt => opt.MapFrom(src => src.Company!.Name)
            );
    }
}
