using System.Text.Json.Serialization;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Seeding;

/// <summary>
/// The identity rows a SQLite seed writes (<c>SeedShiftIdentitySqliteAsync</c>). Records reference each other by
/// <c>Key</c>, a name local to the seed that is never stored; users are referenced by <c>Username</c>. Every record
/// may carry an explicit <c>ID</c>. A record matches an existing row by <c>ID</c> when it has one, otherwise by its
/// natural key (<c>Username</c> for users, <c>AppId</c> for apps, <c>IntegrationId</c> when given, otherwise
/// <c>Name</c> within its parent). The authority's runtime security tables are not seedable: each new user gets its
/// security row from the seed.
/// </summary>
public sealed class IdentitySeedData
{
    /// <summary>
    /// <c>false</c> (the default): a record that matches an existing row leaves the row as it is. <c>true</c>: the
    /// matched row is updated to the record's values. A link list (a branch's brands, a user's teams, …) that the
    /// record leaves out (<c>null</c>) is not touched either way.
    /// </summary>
    public bool UpdateExisting { get; set; }

    public List<CountrySeed> Countries { get; set; } = [];
    public List<RegionSeed> Regions { get; set; } = [];
    public List<CitySeed> Cities { get; set; } = [];
    public List<CompanySeed> Companies { get; set; } = [];
    public List<BrandSeed> Brands { get; set; } = [];
    public List<DepartmentSeed> Departments { get; set; } = [];
    public List<ServiceSeed> Services { get; set; } = [];
    public List<CompanyBranchSeed> CompanyBranches { get; set; } = [];
    public List<TeamSeed> Teams { get; set; } = [];
    public List<AccessTreeSeed> AccessTrees { get; set; } = [];
    public List<AppSeed> Apps { get; set; } = [];
    public List<UserSeed> Users { get; set; } = [];
    public List<CompanyCalendarSeed> CompanyCalendars { get; set; } = [];
}

public sealed class CountrySeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    public string? ShortCode { get; set; }
    public string CallingCode { get; set; } = default!;
    public string? IntegrationId { get; set; }
    public string? Flag { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsProtected { get; set; }
}

public sealed class RegionSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>The <c>Key</c> of a country in this seed.</summary>
    public string? Country { get; set; }
    public string? ShortCode { get; set; }
    public string? IntegrationId { get; set; }
    public string? Flag { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsProtected { get; set; }
}

public sealed class CitySeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>The <c>Key</c> of a region in this seed. The city's country is the region's.</summary>
    public string? Region { get; set; }
    public string? IntegrationId { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsProtected { get; set; }
}

public sealed class CompanySeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? ShortCode { get; set; }
    public string? IntegrationId { get; set; }
    public CompanyTypes CompanyType { get; set; }
    /// <summary>The <c>Key</c> of another company in this seed.</summary>
    public string? ParentCompany { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
    public string? Website { get; set; }
    public DateTime? TerminationDate { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsProtected { get; set; }
}

public sealed class BrandSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}

public sealed class DepartmentSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}

public sealed class ServiceSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    public string? IntegrationId { get; set; }
}

public sealed class CompanyBranchSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>The <c>Key</c> of a company in this seed. Required.</summary>
    public string? Company { get; set; }
    /// <summary>The <c>Key</c> of a city in this seed. The branch's region and country are the city's.</summary>
    public string? City { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? ShortCode { get; set; }
    public string? IntegrationId { get; set; }
    public string? Phone { get; set; }
    public string? ShortPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public string? WorkingHours { get; set; }
    public string? WorkingDays { get; set; }
    public DateTime? TerminationDate { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsProtected { get; set; }
    /// <summary>Keys of brands in this seed.</summary>
    public List<string>? Brands { get; set; }
    /// <summary>Keys of departments in this seed.</summary>
    public List<string>? Departments { get; set; }
    /// <summary>Keys of services in this seed.</summary>
    public List<string>? Services { get; set; }
}

public sealed class TeamSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>The <c>Key</c> of a company in this seed.</summary>
    public string? Company { get; set; }
    public string? IntegrationId { get; set; }
    public List<string>? Tags { get; set; }
    /// <summary>Keys of company branches in this seed.</summary>
    public List<string>? CompanyBranches { get; set; }
    /// <summary>Usernames of users in this seed. A user's own <c>Teams</c> list adds to this.</summary>
    public List<string>? Users { get; set; }
}

public sealed class AccessTreeSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>Full access to every registered action tree, built when the seed runs. Instead of <see cref="Tree"/>.</summary>
    public bool FullAccess { get; set; }
    /// <summary>The access tree as JSON: a JSON object in a JSON seed, a JSON string in C# (either is read from JSON).</summary>
    [JsonConverter(typeof(IdentitySeedJson.ObjectOrStringConverter))]
    public string? Tree { get; set; }
}

public sealed class AppSeed
{
    public long? ID { get; set; }
    /// <summary>The app's client ID; also how the seed matches an existing app.</summary>
    public string AppId { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
    public string? Description { get; set; }
    public string? AppSecret { get; set; }
}

public sealed class UserSeed
{
    public long? ID { get; set; }
    public string Username { get; set; } = default!;
    /// <summary>Hashed when the seed runs. Or <see cref="PasswordHash"/> and <see cref="Salt"/>. Required for a new user.</summary>
    public string? Password { get; set; }
    public byte[]? PasswordHash { get; set; }
    public byte[]? Salt { get; set; }
    /// <summary>Defaults to the username.</summary>
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? Phone { get; set; }
    public bool PhoneVerified { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? IntegrationId { get; set; }
    public string? Signature { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsProtected { get; set; }
    public bool AllowDeviceSignIn { get; set; }
    public bool RequireChangePassword { get; set; }
    /// <summary>The <c>Key</c> of a company branch in this seed. The user's company, region and country are the branch's.</summary>
    public string? CompanyBranch { get; set; }
    /// <summary>The user's own access tree, as JSON (object or string), besides the named <see cref="AccessTrees"/>.</summary>
    [JsonConverter(typeof(IdentitySeedJson.ObjectOrStringConverter))]
    public string? AccessTree { get; set; }
    /// <summary>Keys of access trees in this seed.</summary>
    public List<string>? AccessTrees { get; set; }
    /// <summary>Keys of teams in this seed.</summary>
    public List<string>? Teams { get; set; }
}

public sealed class CompanyCalendarSeed
{
    public long? ID { get; set; }
    public string? Key { get; set; }
    public string Title { get; set; } = default!;
    /// <summary>The <c>Key</c> of a company in this seed.</summary>
    public string? Company { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public CalendarEntryType EntryType { get; set; } = CalendarEntryType.Workday;
    public int Priority { get; set; }
    /// <summary>Keys of company branches in this seed.</summary>
    public List<string>? CompanyBranches { get; set; }
    public List<CompanyCalendarShiftGroupSeed> ShiftGroups { get; set; } = [];
    public List<CompanyCalendarWeekendGroupSeed> WeekendGroups { get; set; } = [];
}

public sealed class CompanyCalendarShiftGroupSeed
{
    /// <summary>Keys of departments in this seed.</summary>
    public List<string> Departments { get; set; } = [];
    /// <summary>Keys of brands in this seed.</summary>
    public List<string> Brands { get; set; } = [];
    public List<int>? Days { get; set; }
    public List<CompanyCalendarShift> Shifts { get; set; } = [];
}

public sealed class CompanyCalendarWeekendGroupSeed
{
    /// <summary>Keys of departments in this seed.</summary>
    public List<string> Departments { get; set; } = [];
    /// <summary>Keys of brands in this seed.</summary>
    public List<string> Brands { get; set; } = [];
    public List<CompanyCalendarWeekendRule> Weekends { get; set; } = [];
}
