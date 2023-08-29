
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
                )
            .ForMember(
                    dest => dest.CompanyBranchID,
                    opt => opt.MapFrom(src => !src.CompanyBranchID.HasValue ? null : new ShiftEntitySelectDTO { Value = src.CompanyBranchID.ToString()!, Text = null })
                );

        CreateMap<ShiftIdentity.AspNetCore.Entities.User, UserListDTO>()
            .ForMember(
                    dest => dest.CompanyBranch,
                    opt => opt.MapFrom(src => !src.CompanyBranchID.HasValue ? null : src.CompanyBranch!.Name)
                );

        CreateMap<ShiftIdentity.AspNetCore.Entities.User, UserDataDTO>();
    }
}
