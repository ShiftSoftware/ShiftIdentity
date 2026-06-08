
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
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
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value= src.CompanyBranchID.ToString()!, Text = src.CompanyBranch!.Name })
                );

        CreateMap<Core.Entities.User, UserListDTO>()
            .ForMember(
                    dest => dest.CompanyBranch,
                    opt => opt.MapFrom(src => src.CompanyBranch!.Name)
                )
            .ForMember(
                dest => dest.LastSeen,
                opt => opt.MapFrom(x => (x.UserLog == null || x.UserLog.LastSeen == null ? x.LastSeen : x.UserLog.LastSeen))
            )
            .ForMember(
                dest => dest.AccessTrees,
                opt => opt.MapFrom(src => src.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree!.Name }))
            );

        CreateMap<Core.Entities.User, UserModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );

        CreateMap<Core.Entities.User, UserDataDTO>()
            .ReverseMap()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.Username, o => o.Ignore())
            .ForMember(d => d.Email, o => o.Ignore())
            .ForMember(d => d.Phone, o => o.Ignore())
            .ForMember(d => d.EmailVerified, o => o.Ignore())
            .ForMember(d => d.PhoneVerified, o => o.Ignore())
            .ForMember(d => d.LastSavedByUserID, o => o.Ignore())
            .ForMember(d => d.CreatedByUserID, o => o.Ignore())
            .ForMember(d => d.LastSaveDate, o => o.Ignore())
            .ForMember(d => d.CreateDate, o => o.Ignore());
        CreateMap<Core.Entities.User, UserInfoDTO>();
    }
}
