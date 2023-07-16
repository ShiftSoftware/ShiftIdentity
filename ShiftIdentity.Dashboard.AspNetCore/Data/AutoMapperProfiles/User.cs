
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class User : Profile
{
	public User()
	{
        CreateMap<ShiftIdentity.AspNetCore.Entities.User, UserDTO>()
            .ForMember(
                    dest => dest.AccessTrees,
                    opt => opt.MapFrom(src => src.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString(), Text = y.AccessTree.Name }))
                );

        CreateMap<ShiftIdentity.AspNetCore.Entities.User, UserListDTO>();
        CreateMap<ShiftIdentity.AspNetCore.Entities.User, UserDataDTO>();
    }
}
