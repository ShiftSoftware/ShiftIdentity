using AutoMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class CompanyRepository :
    ShiftRepository<ShiftIdentityDB, Company, CompanyListDTO, CompanyDTO, CompanyDTO>,
    IShiftRepositoryAsync<Company, CompanyListDTO, CompanyDTO>
{
    public CompanyRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Companies, mapper)
    {

    }

    public override ValueTask<Company> UpsertAsync(Company entity, CompanyDTO dto, ActionTypes actionType, long? userId = null)
    {
        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.HQPhone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));
        }

        return base.UpsertAsync(entity, dto, actionType, userId);
    }
}
