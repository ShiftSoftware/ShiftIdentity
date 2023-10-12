
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Company : Profile
{
    public Company()
    {
        CreateMap<Core.Entities.Company, CompanyDTO>();

        CreateMap<CompanyDTO, Core.Entities.Company>().ForMember(
            m => m.HQPhone,
            opt => opt.MapFrom(x => x.HQPhone == null ? null : Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(x.HQPhone))
        );

        CreateMap<Core.Entities.Company, CompanyListDTO>();

        CreateMap<Core.Entities.Company, CompanyModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.CompanyID,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.BranchID,
                opt => opt.MapFrom(src => "")
            )
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => CompanyItemTypes.Company)
            );
    }
}
