using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class General : Profile
{
    public General()
    {
        CreateMap<CompanyBranchService, CompanyBranchServiceModel>()
            .ForMember(dest => dest.ID, opt => opt.MapFrom(src => src.ServiceID))
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => "Service"))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Service.Name))
            .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => src.Service.IsDeleted))
            .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.Service.CreateDate))
            .ForMember(dest => dest.LastSaveDate, opt => opt.MapFrom(src => src.Service.LastSaveDate))
            .ForMember(dest => dest.CreatedByUserID, opt => opt.MapFrom(src => src.Service.CreatedByUserID))
            .ForMember(dest => dest.LastSavedByUserID, opt => opt.MapFrom(src => src.Service.LastSavedByUserID));
    }
}
