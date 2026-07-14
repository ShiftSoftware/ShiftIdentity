using System;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Core;

public class ShiftIdentityFeatureLocking
{
    public bool CountryFeatureIsLocked { get; set; }
    public bool RegionFeatureIsLocked { get; set; }
    public bool CityFeatureIsLocked { get; set; }
    public bool BrandFeatureIsLocked { get; set; }
    public bool DepartmentFeatureIsLocked { get; set; }
    public bool ServiceFeatureIsLocked { get; set; }
    public bool CompanyFeatureIsLocked { get; set; }
    public bool CompanyBranchFeatureIsLocked { get; set; }
    public bool AppFeatureIsLocked { get; set; }
    public bool AccessTreeFeatureIsLocked { get; set; }
    public bool UserFeatureIsLocked { get; set; }
    public bool TeamFeatureIsLocked { get; set; }
    public bool CompanyCalendarFeatureIsLocked { get; set; }

    // Entity type → (is-this-feature-locked, localization key) map. This is what lets the generalized
    // FeatureLockSaveValidator enforce feature locking on every repository save WITHOUT a per-repository
    // SaveChangesAsync override. The message keys are the exact strings the old per-repository overrides threw,
    // so existing localization entries keep resolving.
    private static readonly (Type EntityType, Func<ShiftIdentityFeatureLocking, bool> IsLocked, string MessageKey)[] FeatureMap =
    {
        (typeof(Country),         f => f.CountryFeatureIsLocked,         "Country Feature is locked"),
        (typeof(Region),          f => f.RegionFeatureIsLocked,          "Region Feature is locked"),
        (typeof(City),            f => f.CityFeatureIsLocked,            "City Feature is locked"),
        (typeof(Brand),           f => f.BrandFeatureIsLocked,           "Brand Feature is locked"),
        (typeof(Department),      f => f.DepartmentFeatureIsLocked,      "Department Feature is locked"),
        (typeof(Service),         f => f.ServiceFeatureIsLocked,         "Service Feature is locked"),
        (typeof(Company),         f => f.CompanyFeatureIsLocked,         "Company Feature is locked"),
        (typeof(CompanyBranch),   f => f.CompanyBranchFeatureIsLocked,   "Company Branch Feature is locked"),
        (typeof(App),             f => f.AppFeatureIsLocked,             "App Feature is locked"),
        (typeof(AccessTree),      f => f.AccessTreeFeatureIsLocked,      "Access Tree Feature is locked"),
        (typeof(User),            f => f.UserFeatureIsLocked,            "User Feature is locked"),
        (typeof(Team),            f => f.TeamFeatureIsLocked,            "Team Feature is locked"),
        (typeof(CompanyCalendar), f => f.CompanyCalendarFeatureIsLocked, "Company Calendar feature is locked."),
    };

    /// <summary>
    /// Returns the localization key for the "feature is locked" message when writes to <paramref name="entityType"/>
    /// are currently locked, or <see langword="null"/> when the type isn't feature-lockable or its feature is unlocked.
    /// </summary>
    public string? GetLockedMessageKey(Type entityType)
    {
        foreach (var (type, isLocked, messageKey) in FeatureMap)
            if (type == entityType && isLocked(this))
                return messageKey;

        return null;
    }
}
