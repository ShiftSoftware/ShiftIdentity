
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class User : Profile
{
    public User()
    {
        CreateMap<Core.Entities.User, UserDTO>()
            .ForMember(
                    dest => dest.AccessTrees,
                    opt => opt.MapFrom(src => src.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString(), Text = y.AccessTree.Name }))
                )
            .ForMember(
                    dest => dest.CompanyBranchID,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.CompanyBranchID.ToString()!, Text = null })
                );

        CreateMap<Core.Entities.User, UserListDTO>()
            .ForMember(
                    dest => dest.CompanyBranch,
                    opt => opt.MapFrom(src => src.CompanyBranch!.Name)
                );

        CreateMap<Core.Entities.User, UserDataDTO>();
    }
}
