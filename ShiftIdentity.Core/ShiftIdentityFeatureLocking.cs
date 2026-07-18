using System;

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

    // Entity-type-NAME → (is-this-feature-locked, localization key) map. This is what lets the generalized
    // FeatureLockSaveValidator enforce feature locking on every repository save WITHOUT a per-repository
    // SaveChangesAsync override. The message keys are the exact strings the old per-repository overrides threw,
    // so existing localization entries keep resolving.
    //
    // Keyed by the entity type's simple NAME (not typeof(...)) so this config class — part of the ShiftIdentity.Core
    // configuration surface — does not depend on the entity types, which live in ShiftIdentity.Data. The entity
    // names are unique, and FeatureLockSaveValidator passes the concrete entity type, so name-matching is
    // behaviorally identical to the previous typeof() comparison.
    private static readonly (string EntityTypeName, Func<ShiftIdentityFeatureLocking, bool> IsLocked, string MessageKey)[] FeatureMap =
    {
        ("Country",         f => f.CountryFeatureIsLocked,         "Country Feature is locked"),
        ("Region",          f => f.RegionFeatureIsLocked,          "Region Feature is locked"),
        ("City",            f => f.CityFeatureIsLocked,            "City Feature is locked"),
        ("Brand",           f => f.BrandFeatureIsLocked,           "Brand Feature is locked"),
        ("Department",      f => f.DepartmentFeatureIsLocked,      "Department Feature is locked"),
        ("Service",         f => f.ServiceFeatureIsLocked,         "Service Feature is locked"),
        ("Company",         f => f.CompanyFeatureIsLocked,         "Company Feature is locked"),
        ("CompanyBranch",   f => f.CompanyBranchFeatureIsLocked,   "Company Branch Feature is locked"),
        ("App",             f => f.AppFeatureIsLocked,             "App Feature is locked"),
        ("AccessTree",      f => f.AccessTreeFeatureIsLocked,      "Access Tree Feature is locked"),
        ("User",            f => f.UserFeatureIsLocked,            "User Feature is locked"),
        ("Team",            f => f.TeamFeatureIsLocked,            "Team Feature is locked"),
        ("CompanyCalendar", f => f.CompanyCalendarFeatureIsLocked, "Company Calendar feature is locked."),
    };

    /// <summary>
    /// Returns the localization key for the "feature is locked" message when writes to <paramref name="entityType"/>
    /// are currently locked, or <see langword="null"/> when the type isn't feature-lockable or its feature is unlocked.
    /// </summary>
    public string? GetLockedMessageKey(Type entityType)
    {
        foreach (var (name, isLocked, messageKey) in FeatureMap)
            if (name == entityType.Name && isLocked(this))
                return messageKey;

        return null;
    }
}
