namespace ShiftSoftware.ShiftIdentity.Data.Seeding;

/// <summary>
/// Checks a seed before anything is written: required values, duplicate keys and IDs, references to records that
/// are not in the seed, and cycles in parent companies. Every problem is reported, each naming its record.
/// Whether a user is new (and so needs a password) depends on the database, so the seeder checks that itself.
/// </summary>
public static class IdentitySeedValidator
{
    public static IReadOnlyList<string> Validate(IdentitySeedData seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var problems = new List<string>();

        static string Name(string table, int index, string? label) => label is { Length: > 0 } ? $"{table}[{index}] ({label})" : $"{table}[{index}]";

        // Keys and IDs within one table; references then resolve against these sets.
        HashSet<string> Keys<T>(string table, List<T> records, Func<T, string?> key, Func<T, string?> label)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < records.Count; i++)
                if (key(records[i]) is { } k && !keys.Add(k))
                    problems.Add($"{Name(table, i, label(records[i]))}: the key '{k}' is used by another record in {table}.");
            return keys;
        }

        void IDs<T>(string table, List<T> records, Func<T, long?> id, Func<T, string?> label)
        {
            var ids = new HashSet<long>();
            for (var i = 0; i < records.Count; i++)
            {
                if (id(records[i]) is not { } value) continue;
                if (value <= 0) problems.Add($"{Name(table, i, label(records[i]))}: the ID {value} is not positive.");
                else if (!ids.Add(value)) problems.Add($"{Name(table, i, label(records[i]))}: the ID {value} is used by another record in {table}.");
            }
        }

        void Required<T>(string table, List<T> records, Func<T, string?> value, string property, Func<T, string?> label)
        {
            for (var i = 0; i < records.Count; i++)
                if (string.IsNullOrWhiteSpace(value(records[i])))
                    problems.Add($"{Name(table, i, label(records[i]))}: {property} is required.");
        }

        void Reference(string table, int index, string? label, string property, string? key, HashSet<string> known, string target)
        {
            if (key is not null && !known.Contains(key))
                problems.Add($"{Name(table, index, label)}: {property} '{key}' is not the key of a record in {target}.");
        }

        void References(string table, int index, string? label, string property, IEnumerable<string>? keys, HashSet<string> known, string target)
        {
            foreach (var key in keys ?? []) Reference(table, index, label, property, key, known, target);
        }

        var countries = Keys("Countries", seed.Countries, x => x.Key, x => x.Name);
        var regions = Keys("Regions", seed.Regions, x => x.Key, x => x.Name);
        var cities = Keys("Cities", seed.Cities, x => x.Key, x => x.Name);
        var companies = Keys("Companies", seed.Companies, x => x.Key, x => x.Name);
        var brands = Keys("Brands", seed.Brands, x => x.Key, x => x.Name);
        var departments = Keys("Departments", seed.Departments, x => x.Key, x => x.Name);
        var services = Keys("Services", seed.Services, x => x.Key, x => x.Name);
        var branches = Keys("CompanyBranches", seed.CompanyBranches, x => x.Key, x => x.Name);
        var teams = Keys("Teams", seed.Teams, x => x.Key, x => x.Name);
        var accessTrees = Keys("AccessTrees", seed.AccessTrees, x => x.Key, x => x.Name);
        Keys("CompanyCalendars", seed.CompanyCalendars, x => x.Key, x => x.Title);

        // Users and apps are referenced and matched by username and app ID. The unique indexes ignore case on SQL Server.
        var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < seed.Users.Count; i++)
            if (seed.Users[i].Username is { Length: > 0 } u && !usernames.Add(u))
                problems.Add($"{Name("Users", i, u)}: the username is used by another record in Users.");
        var appIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < seed.Apps.Count; i++)
            if (seed.Apps[i].AppId is { Length: > 0 } a && !appIds.Add(a))
                problems.Add($"{Name("Apps", i, a)}: the app ID is used by another record in Apps.");

        IDs("Countries", seed.Countries, x => x.ID, x => x.Name);
        IDs("Regions", seed.Regions, x => x.ID, x => x.Name);
        IDs("Cities", seed.Cities, x => x.ID, x => x.Name);
        IDs("Companies", seed.Companies, x => x.ID, x => x.Name);
        IDs("Brands", seed.Brands, x => x.ID, x => x.Name);
        IDs("Departments", seed.Departments, x => x.ID, x => x.Name);
        IDs("Services", seed.Services, x => x.ID, x => x.Name);
        IDs("CompanyBranches", seed.CompanyBranches, x => x.ID, x => x.Name);
        IDs("Teams", seed.Teams, x => x.ID, x => x.Name);
        IDs("AccessTrees", seed.AccessTrees, x => x.ID, x => x.Name);
        IDs("Apps", seed.Apps, x => x.ID, x => x.AppId);
        IDs("Users", seed.Users, x => x.ID, x => x.Username);
        IDs("CompanyCalendars", seed.CompanyCalendars, x => x.ID, x => x.Title);

        Required("Countries", seed.Countries, x => x.Name, "Name", x => x.Key);
        Required("Countries", seed.Countries, x => x.CallingCode, "CallingCode", x => x.Name);
        Required("Regions", seed.Regions, x => x.Name, "Name", x => x.Key);
        Required("Cities", seed.Cities, x => x.Name, "Name", x => x.Key);
        Required("Companies", seed.Companies, x => x.Name, "Name", x => x.Key);
        Required("Brands", seed.Brands, x => x.Name, "Name", x => x.Key);
        Required("Departments", seed.Departments, x => x.Name, "Name", x => x.Key);
        Required("Services", seed.Services, x => x.Name, "Name", x => x.Key);
        Required("CompanyBranches", seed.CompanyBranches, x => x.Name, "Name", x => x.Key);
        Required("CompanyBranches", seed.CompanyBranches, x => x.Company, "Company", x => x.Name);
        // Sign-in puts the user's company, region, branch and the branch's city in the session; the region comes from the city.
        Required("CompanyBranches", seed.CompanyBranches, x => x.City, "City (sign-in needs the branch's city and region)", x => x.Name);
        Required("Teams", seed.Teams, x => x.Name, "Name", x => x.Key);
        Required("AccessTrees", seed.AccessTrees, x => x.Name, "Name", x => x.Key);
        Required("Apps", seed.Apps, x => x.AppId, "AppId", x => x.DisplayName);
        Required("Apps", seed.Apps, x => x.DisplayName, "DisplayName", x => x.AppId);
        Required("Apps", seed.Apps, x => x.RedirectUri, "RedirectUri", x => x.AppId);
        Required("Users", seed.Users, x => x.Username, "Username", x => x.FullName);
        Required("Users", seed.Users, x => x.CompanyBranch, "CompanyBranch (sign-in needs the user's company, region and branch)", x => x.Username);
        Required("CompanyCalendars", seed.CompanyCalendars, x => x.Title, "Title", x => x.Key);

        for (var i = 0; i < seed.Regions.Count; i++)
            Reference("Regions", i, seed.Regions[i].Name, "Country", seed.Regions[i].Country, countries, "Countries");
        for (var i = 0; i < seed.Cities.Count; i++)
            Reference("Cities", i, seed.Cities[i].Name, "Region", seed.Cities[i].Region, regions, "Regions");
        for (var i = 0; i < seed.Companies.Count; i++)
            Reference("Companies", i, seed.Companies[i].Name, "ParentCompany", seed.Companies[i].ParentCompany, companies, "Companies");
        for (var i = 0; i < seed.CompanyBranches.Count; i++)
        {
            var b = seed.CompanyBranches[i];
            Reference("CompanyBranches", i, b.Name, "Company", b.Company, companies, "Companies");
            Reference("CompanyBranches", i, b.Name, "City", b.City, cities, "Cities");
            References("CompanyBranches", i, b.Name, "Brands", b.Brands, brands, "Brands");
            References("CompanyBranches", i, b.Name, "Departments", b.Departments, departments, "Departments");
            References("CompanyBranches", i, b.Name, "Services", b.Services, services, "Services");
        }
        for (var i = 0; i < seed.Teams.Count; i++)
        {
            var t = seed.Teams[i];
            Reference("Teams", i, t.Name, "Company", t.Company, companies, "Companies");
            References("Teams", i, t.Name, "CompanyBranches", t.CompanyBranches, branches, "CompanyBranches");
            References("Teams", i, t.Name, "Users", t.Users, usernames, "Users");
        }
        for (var i = 0; i < seed.AccessTrees.Count; i++)
        {
            var a = seed.AccessTrees[i];
            if (a.FullAccess && a.Tree is not null)
                problems.Add($"{Name("AccessTrees", i, a.Name)}: FullAccess and Tree are both given; give one.");
            else if (!a.FullAccess && string.IsNullOrWhiteSpace(a.Tree))
                problems.Add($"{Name("AccessTrees", i, a.Name)}: Tree or FullAccess is required.");
        }
        for (var i = 0; i < seed.Users.Count; i++)
        {
            var u = seed.Users[i];
            Reference("Users", i, u.Username, "CompanyBranch", u.CompanyBranch, branches, "CompanyBranches");
            References("Users", i, u.Username, "AccessTrees", u.AccessTrees, accessTrees, "AccessTrees");
            References("Users", i, u.Username, "Teams", u.Teams, teams, "Teams");
            if (u.Password is not null && (u.PasswordHash is not null || u.Salt is not null))
                problems.Add($"{Name("Users", i, u.Username)}: Password and PasswordHash/Salt are both given; give one.");
            else if ((u.PasswordHash is null) != (u.Salt is null))
                problems.Add($"{Name("Users", i, u.Username)}: PasswordHash and Salt go together.");
            else if (u.Password is { Length: 0 })
                problems.Add($"{Name("Users", i, u.Username)}: Password is empty.");
        }
        for (var i = 0; i < seed.CompanyCalendars.Count; i++)
        {
            var c = seed.CompanyCalendars[i];
            Reference("CompanyCalendars", i, c.Title, "Company", c.Company, companies, "Companies");
            References("CompanyCalendars", i, c.Title, "CompanyBranches", c.CompanyBranches, branches, "CompanyBranches");
            if (c.EndDate < c.StartDate)
                problems.Add($"{Name("CompanyCalendars", i, c.Title)}: EndDate is before StartDate.");
            foreach (var g in c.ShiftGroups)
            {
                References("CompanyCalendars", i, c.Title, "ShiftGroups.Brands", g.Brands, brands, "Brands");
                References("CompanyCalendars", i, c.Title, "ShiftGroups.Departments", g.Departments, departments, "Departments");
            }
            foreach (var g in c.WeekendGroups)
            {
                References("CompanyCalendars", i, c.Title, "WeekendGroups.Brands", g.Brands, brands, "Brands");
                References("CompanyCalendars", i, c.Title, "WeekendGroups.Departments", g.Departments, departments, "Departments");
            }
        }

        // A company may not be its own ancestor.
        var parents = seed.Companies.Where(x => x.Key is not null && x.ParentCompany is not null)
            .GroupBy(x => x.Key!).ToDictionary(x => x.Key, x => x.First().ParentCompany!);
        var reported = new HashSet<string>();
        foreach (var start in parents.Keys)
        {
            var seen = new List<string> { start };
            for (var at = start; parents.TryGetValue(at, out var parent);)
            {
                if (parent == start)
                {
                    if (seen.All(reported.Add))
                        problems.Add($"Companies: the parent companies form a cycle: {string.Join(" → ", seen.Append(start))}.");
                    break;
                }
                if (seen.Contains(parent)) break;   // a cycle further up; reported from one of its own members
                seen.Add(parent);
                at = parent;
            }
        }

        return problems;
    }
}
