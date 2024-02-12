using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserGroup;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class UserGroup : Profile
{
    public UserGroup()
    {
        CreateMap<Core.Entities.UserGroup, UserGroupDTO>()
            .ForMember(x => x.Users, opt => opt.MapFrom(x => x.UserGroupUsers.Select(y =>
            new ShiftEntitySelectDTO { Value = y.User.ID.ToString()!, Text = y.User.Username })))
            .ReverseMap()
            .ForMember(x => x.UserGroupUsers, m => m.MapFrom(x => x.Users.Select(s =>
            new UserGroupUser
            {
                UserID = s.Value.ToLong()
            }
            )));
    }
}
