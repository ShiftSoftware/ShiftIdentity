using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class CompanyRepository :
    ShiftRepository<ShiftIdentityDB, Company>,
    IShiftRepositoryAsync<Company, CompanyListDTO, CompanyDTO>
{
    private readonly ShiftIdentityDB db;
    public CompanyRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Companies, mapper)
    {
        this.db = db;
    }

    public ValueTask<Company> CreateAsync(CompanyDTO dto, long? userId = null)
    {
        var entity = new Company().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Company>(entity);
    }

    public ValueTask<Company> DeleteAsync(Company entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<Company>(entity);
    }

    public async Task<Company> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
    {
        return await base.FindAsync(id, asOf, ignoreGlobalFilters: ignoreGlobalFilters);
    }

    public IQueryable<CompanyListDTO> OdataList(bool ignoreGlobalFilters = false)
    {
        var data = db.Companies.AsNoTracking();

        if (ignoreGlobalFilters)
            data = data.IgnoreQueryFilters();

        return mapper.ProjectTo<CompanyListDTO>(data);
    }

    public ValueTask<Company> UpdateAsync(Company entity, CompanyDTO dto, long? userId = null)
    {
        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Company>(entity);
    }

    public ValueTask<CompanyDTO> ViewAsync(Company entity)
    {
        return new ValueTask<CompanyDTO>(mapper.Map<CompanyDTO>(entity));
    }

    private void AssignValues(CompanyDTO dto, Company entity)
    {
        entity.Name = dto.Name;
        entity.LegalName = dto.LegalName;
        entity.ShortCode = dto.ShortCode;
        entity.ExternalId = dto.ExternalId;
        entity.AlternativeExternalId = dto.AlternativeExternalId;
        entity.CompanyType = dto.CompanyType;
        
        entity.HQEmail = dto.HQEmail;
        entity.HQAddress = dto.HQAddress;

        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!UserRepository.PhoneIsValid(dto.HQPhone))
                throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

            entity.HQPhone = UserRepository.GetFormattedPhone(dto.HQPhone);
        }
    }
}
