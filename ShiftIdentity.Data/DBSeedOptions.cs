using ShiftSoftware.ShiftEntity.Model.Enums;

namespace ShiftSoftware.ShiftIdentity.Data;

public class DBSeedOptions
{
    public string? CountryShortCode { get; set; }
    public string? CountryExternalId { get; set; }
    public string? CountryCallingCode { get; set; }

    public string? RegionShortCode { get; set; }
    public string? RegionExternalId { get; set; }

    public string? CompanyShortCode { get; set; }
    public string? CompanyExternalId { get; set; }
    public string? CompanyAlternativeExternalId { get; set; }
    public CompanyTypes CompanyType { get; set; }

    public string? CompanyBranchShortCode { get; set; }
    public string? CompanyBranchExternalId { get; set; }
}