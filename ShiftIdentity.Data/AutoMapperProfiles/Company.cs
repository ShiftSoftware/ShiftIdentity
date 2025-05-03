
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Company : Profile
{
    public Company()
    {
        CreateMap<Core.Entities.Company, CompanyDTO>()
            .ForMember(
                    dest => dest.ParentCompany,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.ParentCompanyID.ToString()!})
                )
            .ForMember(
                dest => dest.CustomFields,
                opt => opt.MapFrom(src => src.CustomFields == null ? null : src.CustomFields
                .ToDictionary(x => x.Key, x =>
                new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                }))
            );

        CreateMap<CompanyDTO, Core.Entities.Company>()
            .ForMember(
                m => m.HQPhone,
                opt => opt.MapFrom(x => x.HQPhone == null ? null : Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(x.HQPhone))
            )
            .ForMember(x=> x.CustomFields, x=> x.Ignore())
            .AfterMap((src, dest) =>
            {
                // Assuming both src and dest have a property CustomFields of type Dictionary or similar
                foreach (var key in src.CustomFields.Keys)
                {
                    var srcField = src.CustomFields[key];
                    // Check if the field is a password and its value is null
                    if (srcField.IsPassword && srcField.Value == null)
                        continue;

                    dest.CustomFields[key] = new CustomField
                    {
                        Value = srcField.Value,
                        DisplayName = srcField.DisplayName,
                        IsPassword = srcField.IsPassword,
                        IsEncrypted = srcField.IsEncrypted
                    };
                }
            });

        CreateMap<Core.Entities.Company, CompanyListDTO>()
            .ForMember(
                m => m.ParentCompanyName,
                opt => opt.MapFrom(x => x.ParentCompany == null ? null : x.ParentCompany.Name)
            );

        CreateMap<Core.Entities.Company, CompanyModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
