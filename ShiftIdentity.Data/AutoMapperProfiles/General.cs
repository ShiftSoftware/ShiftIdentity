using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class General : Profile
{
    public General()
    {
        CreateMap<CompanyBranchService, CompanyBranchServiceModel>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => "Service"))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Service.Name))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ServiceID.ToString())
            );
    }
}
