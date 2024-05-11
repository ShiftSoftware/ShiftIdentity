using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Team : Profile
{
    public Team()
    {
        CreateMap<Core.Entities.Team, TeamDTO>()
            .ForMember(x => x.Users, opt => opt.MapFrom(x => x.TeamUsers.Select(y =>
            new ShiftEntitySelectDTO { Value = y.User.ID.ToString()!, Text = y.User.Username })))
            .ReverseMap()
            .ForMember(x => x.TeamUsers, m => m.MapFrom(x => x.Users.Select(s =>
            new TeamUser
            {
                UserID = s.Value.ToLong()
            }
            )));

        CreateMap<Core.Entities.Team, TeamModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
