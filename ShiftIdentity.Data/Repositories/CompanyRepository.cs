using AutoMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CompanyRepository : ShiftRepository<ShiftIdentityDbContext, Company, CompanyListDTO, CompanyDTO>
{
    public CompanyRepository(ShiftIdentityDbContext db, IMapper mapper) : base(db, mapper)
    {

    }

    public override ValueTask<Company> UpsertAsync(Company entity, CompanyDTO dto, ActionTypes actionType, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.HQPhone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));
        }

        return base.UpsertAsync(entity, dto, actionType, userId);
    }

    public override ValueTask<Company> DeleteAsync(Company entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }
}
