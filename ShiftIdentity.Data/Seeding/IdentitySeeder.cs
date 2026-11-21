using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Seeding;

/// <summary>
/// Applies an <see cref="IdentitySeedData"/> to a SQLite database, in one transaction: a failure leaves the database
/// as it was. On any other database it seeds nothing, logs a warning and returns <see cref="IdentitySeedResult.Skipped"/>.
/// Tables are written in dependency order (countries, regions, cities, companies, brands, departments, services,
/// branches, teams, access trees, apps, users, team members, calendars). Hosts call it through
/// <c>SeedShiftIdentitySqliteAsync</c>; tests may call it on a context directly.
/// </summary>
public sealed class IdentitySeeder(ShiftIdentityDbContext db, IEnumerable<Type> actionTrees, ILogger? logger = null)
{
    public async Task<IdentitySeedResult> SeedAsync(IdentitySeedData seed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (db.Database.ProviderName != OtherDatabases.SqliteProviderName)
        {
            logger?.LogWarning("SeedShiftIdentitySqliteAsync was called but the identity DbContext uses {Provider}. The SQLite seed only runs on SQLite; nothing was seeded. SQL Server hosts use SeedDBAsync.",
                db.Database.ProviderName);
            return new() { Skipped = true };
        }

        // The seed runs before the authority's own startup check, so it checks the schema itself.
        var missing = await IdentitySchema.FindMissingTablesAsync(db, ct);
        if (missing.Count > 0)
            throw new InvalidOperationException($"The identity seed was not applied: the identity tables {string.Join(", ", missing)} do not exist. " +
                "Create the schema before seeding, for example with db.Database.EnsureCreated().");

        var problems = IdentitySeedValidator.Validate(seed);
        if (problems.Count > 0) throw new IdentitySeedException(problems);

        var run = new Run(db, seed, FullAccessTree.BuildJson(actionTrees));
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await run.ApplyAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { db.ChangeTracker.Clear(); }

        var result = new IdentitySeedResult { Tables = run.Counts };
        if (logger is not null)
        {
            var written = run.Counts.Where(x => x.Value.Inserted + x.Value.Updated > 0)
                .Select(x => $"{x.Key} {x.Value.Inserted} inserted, {x.Value.Updated} updated").ToList();
            logger.LogInformation("Identity seed applied: {Counts}.", written.Count == 0 ? "nothing to insert or update" : string.Join("; ", written));
        }
        return result;
    }

    /// <summary>One seed call: the rows the seed's keys resolved to, and the counts.</summary>
    private sealed class Run(ShiftIdentityDbContext db, IdentitySeedData seed, string fullAccessTree)
    {
        public Dictionary<string, IdentitySeedTableCount> Counts { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, Country> countries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Region> regions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, City> cities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Company> companies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Brand> brands = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Department> departments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Service> services = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CompanyBranch> branches = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Team> teams = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AccessTree> accessTrees = new(StringComparer.Ordinal);
        private readonly Dictionary<string, User> users = new(StringComparer.OrdinalIgnoreCase);

        public async Task ApplyAsync(CancellationToken ct)
        {
            await CountriesAsync(ct);
            await RegionsAsync(ct);
            await CitiesAsync(ct);
            await CompaniesAsync(ct);
            await NamedAsync<BrandSeed, Brand>("Brands", seed.Brands, brands, x => x.ID, x => x.Key, x => x.Name, x => x.IntegrationId, ct);
            await NamedAsync<DepartmentSeed, Department>("Departments", seed.Departments, departments, x => x.ID, x => x.Key, x => x.Name, x => x.IntegrationId, ct);
            await NamedAsync<ServiceSeed, Service>("Services", seed.Services, services, x => x.ID, x => x.Key, x => x.Name, x => x.IntegrationId, ct);
            await BranchesAsync(ct);
            var writtenTeams = await TeamsAsync(ct);
            await AccessTreesAsync(ct);
            await AppsAsync(ct);
            var writtenUsers = await UsersAsync(ct);
            await TeamMembersAsync(writtenTeams, writtenUsers, ct);
            await CalendarsAsync(ct);
        }

        private void Count(string table, bool inserted)
        {
            Counts.TryGetValue(table, out var count);
            Counts[table] = inserted ? count with { Inserted = count.Inserted + 1 } : count with { Updated = count.Updated + 1 };
        }

        /// <summary>
        /// The row a record stands for: by ID when the record has one, otherwise by its natural key among the rows not
        /// deleted. No row: a new one is added (with the record's ID, when given). <c>Write</c> says whether the
        /// record's values go into the row: always for a new row, for a matched row only with <c>UpdateExisting</c>.
        /// </summary>
        private async Task<(T Row, bool Write)> MatchAsync<T>(string table, long? id, Expression<Func<T, bool>> naturalKey, CancellationToken ct)
            where T : ShiftEntity<T>, new()
        {
            var set = db.Set<T>().IgnoreQueryFilters();
            var row = id is { } value
                ? await set.FirstOrDefaultAsync(x => x.ID == value, ct)
                : await set.Where(x => !x.IsDeleted).Where(naturalKey).OrderBy(x => x.ID).FirstOrDefaultAsync(ct);
            if (row is null)
            {
                // The rows with explicit IDs come first and are saved before the first generated one, so SQLite gives
                // it an ID after them instead of one an explicit ID then collides with.
                if (id is null && db.ChangeTracker.Entries<T>().Any(x => x.State == EntityState.Added && x.Entity.ID > 0))
                    await db.SaveChangesAsync(ct);
                row = new T();
                if (id is { } explicitID) row.ID = explicitID;
                db.Set<T>().Add(row);
                Count(table, inserted: true);
                return (row, true);
            }
            if (!seed.UpdateExisting) return (row, false);
            row.IsDeleted = false;
            Count(table, inserted: false);
            return (row, true);
        }

        /// <summary>The records with an explicit ID first, in seed order, then the others in seed order.</summary>
        private static IEnumerable<TSeed> ExplicitFirst<TSeed>(List<TSeed> records, Func<TSeed, long?> id) => records.OrderBy(x => id(x) is null);

        private static void Remember<T>(Dictionary<string, T> rows, string? key, T row)
        {
            if (key is not null) rows[key] = row;
        }

        private async Task CountriesAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.Countries, x => x.ID))
            {
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<Country>("Countries", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name, ct);
                if (write)
                {
                    row.Name = r.Name; row.ShortCode = r.ShortCode; row.CallingCode = r.CallingCode; row.IntegrationId = r.IntegrationId;
                    row.Flag = r.Flag; row.DisplayOrder = r.DisplayOrder; row.IsProtected = r.IsProtected;
                }
                Remember(countries, r.Key, row);
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task RegionsAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.Regions, x => x.ID))
            {
                var countryID = r.Country is null ? (long?)null : countries[r.Country].ID;
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<Region>("Regions", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name && x.CountryID == countryID, ct);
                if (write)
                {
                    row.Name = r.Name; row.CountryID = countryID; row.ShortCode = r.ShortCode; row.IntegrationId = r.IntegrationId;
                    row.Flag = r.Flag; row.DisplayOrder = r.DisplayOrder; row.IsProtected = r.IsProtected;
                }
                Remember(regions, r.Key, row);
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task CitiesAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.Cities, x => x.ID))
            {
                var region = r.Region is null ? null : regions[r.Region];
                var regionID = region?.ID;
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<City>("Cities", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name && x.RegionID == regionID, ct);
                if (write)
                {
                    row.Name = r.Name; row.RegionID = regionID; row.CountryID = region?.CountryID; row.IntegrationId = r.IntegrationId;
                    row.DisplayOrder = r.DisplayOrder; row.IsProtected = r.IsProtected;
                }
                Remember(cities, r.Key, row);
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task CompaniesAsync(CancellationToken ct)
        {
            var written = new List<(CompanySeed Record, Company Row)>();
            foreach (var r in ExplicitFirst(seed.Companies, x => x.ID))
            {
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<Company>("Companies", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name, ct);
                if (write)
                {
                    row.Name = r.Name; row.LegalName = r.LegalName; row.ShortCode = r.ShortCode; row.IntegrationId = r.IntegrationId;
                    row.CompanyType = r.CompanyType; row.Logo = r.Logo; row.HQPhone = r.HQPhone; row.HQEmail = r.HQEmail;
                    row.HQAddress = r.HQAddress; row.Website = r.Website; row.TerminationDate = r.TerminationDate;
                    row.DisplayOrder = r.DisplayOrder; row.IsProtected = r.IsProtected;
                    written.Add((r, row));
                }
                Remember(companies, r.Key, row);
            }
            await db.SaveChangesAsync(ct);
            // Parents once every company of the seed has its ID.
            foreach (var (r, row) in written)
                row.ParentCompanyID = r.ParentCompany is null ? null : companies[r.ParentCompany].ID;
            await db.SaveChangesAsync(ct);
        }

        private async Task NamedAsync<TSeed, T>(string table, List<TSeed> records, Dictionary<string, T> rows, Func<TSeed, long?> id,
            Func<TSeed, string?> key, Func<TSeed, string> name, Func<TSeed, string?> integrationId, CancellationToken ct)
            where T : ShiftEntity<T>, new()
        {
            foreach (var r in ExplicitFirst(records, id))
            {
                var (n, i) = (name(r), integrationId(r));
                // Brand, Department and Service all have Name and IntegrationId; EF reads them by name.
                var (row, write) = await MatchAsync<T>(table, id(r), i is not null
                    ? x => EF.Property<string?>(x, "IntegrationId") == i
                    : x => EF.Property<string>(x, "Name") == n, ct);
                if (write)
                {
                    var entry = db.Entry(row);
                    entry.Property("Name").CurrentValue = n;
                    entry.Property("IntegrationId").CurrentValue = i;
                }
                Remember(rows, key(r), row);
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task BranchesAsync(CancellationToken ct)
        {
            var written = new List<(CompanyBranchSeed Record, CompanyBranch Row)>();
            foreach (var r in ExplicitFirst(seed.CompanyBranches, x => x.ID))
            {
                var companyID = companies[r.Company!].ID;
                var city = r.City is null ? null : cities[r.City];
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<CompanyBranch>("CompanyBranches", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name && x.CompanyID == companyID, ct);
                if (write)
                {
                    row.Name = r.Name; row.CompanyID = companyID; row.CityID = city?.ID; row.RegionID = city?.RegionID; row.CountryID = city?.CountryID;
                    row.DisplayName = r.DisplayName; row.Description = r.Description; row.ShortCode = r.ShortCode; row.IntegrationId = r.IntegrationId;
                    row.Phone = r.Phone; row.ShortPhone = r.ShortPhone; row.Email = r.Email; row.Address = r.Address;
                    row.Latitude = r.Latitude; row.Longitude = r.Longitude; row.WorkingHours = r.WorkingHours; row.WorkingDays = r.WorkingDays;
                    row.TerminationDate = r.TerminationDate; row.DisplayOrder = r.DisplayOrder; row.IsProtected = r.IsProtected;
                    written.Add((r, row));
                }
                Remember(branches, r.Key, row);
            }
            await db.SaveChangesAsync(ct);

            foreach (var (r, row) in written)
            {
                var branchID = row.ID;
                if (r.Brands is not null)
                    await LinksAsync(db.Set<CompanyBranchBrand>().Where(x => x.CompanyBranchID == branchID), x => x.BrandID,
                        r.Brands.Select(k => brands[k].ID), id => new CompanyBranchBrand { CompanyBranchID = branchID, BrandID = id }, ct);
                if (r.Departments is not null)
                    await LinksAsync(db.Set<CompanyBranchDepartment>().Where(x => x.CompanyBranchID == branchID), x => x.DepartmentID,
                        r.Departments.Select(k => departments[k].ID), id => new CompanyBranchDepartment { CompanyBranchID = branchID, DepartmentID = id }, ct);
                if (r.Services is not null)
                    await LinksAsync(db.Set<CompanyBranchService>().Where(x => x.CompanyBranchID == branchID), x => x.ServiceID,
                        r.Services.Select(k => services[k].ID), id => new CompanyBranchService { CompanyBranchID = branchID, ServiceID = id }, ct);
            }
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Makes a row's links exactly <paramref name="targets"/>: missing links added, others removed.</summary>
        private async Task LinksAsync<TLink>(IQueryable<TLink> current, Func<TLink, long> target, IEnumerable<long> targets,
            Func<long, TLink> create, CancellationToken ct) where TLink : class
        {
            var wanted = targets.ToHashSet();
            var existing = await current.IgnoreQueryFilters().ToListAsync(ct);
            foreach (var link in existing)
                if (!wanted.Remove(target(link))) db.Remove(link);
            foreach (var id in wanted) db.Add(create(id));
        }

        private async Task<List<(TeamSeed Record, Team Row)>> TeamsAsync(CancellationToken ct)
        {
            var written = new List<(TeamSeed Record, Team Row)>();
            foreach (var r in ExplicitFirst(seed.Teams, x => x.ID))
            {
                var companyID = r.Company is null ? (long?)null : companies[r.Company].ID;
                var (name, integrationId) = (r.Name, r.IntegrationId);
                var (row, write) = await MatchAsync<Team>("Teams", r.ID,
                    integrationId is not null ? x => x.IntegrationId == integrationId : x => x.Name == name && x.CompanyID == companyID, ct);
                if (write)
                {
                    row.Name = r.Name; row.CompanyID = companyID; row.IntegrationId = r.IntegrationId;
                    if (r.Tags is not null) row.Tags = [.. r.Tags];
                    written.Add((r, row));
                }
                Remember(teams, r.Key, row);
            }
            await db.SaveChangesAsync(ct);

            foreach (var (r, row) in written)
            {
                var teamID = row.ID;
                if (r.CompanyBranches is not null)
                    await LinksAsync(db.Set<TeamCompanyBranch>().Where(x => x.TeamID == teamID), x => x.CompanyBranchID,
                        r.CompanyBranches.Select(k => branches[k].ID), id => new TeamCompanyBranch { TeamID = teamID, CompanyBranchID = id }, ct);
            }
            await db.SaveChangesAsync(ct);
            return written;
        }

        private async Task AccessTreesAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.AccessTrees, x => x.ID))
            {
                var name = r.Name;
                var (row, write) = await MatchAsync<AccessTree>("AccessTrees", r.ID, x => x.Name == name, ct);
                if (write)
                {
                    row.Name = r.Name;
                    row.Tree = r.FullAccess ? fullAccessTree : r.Tree!;
                }
                Remember(accessTrees, r.Key, row);
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task AppsAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.Apps, x => x.ID))
            {
                var appId = r.AppId;
                var (row, write) = await MatchAsync<App>("Apps", r.ID, x => x.AppId == appId, ct);
                if (write)
                {
                    row.AppId = r.AppId; row.DisplayName = r.DisplayName; row.RedirectUri = r.RedirectUri;
                    row.Description = r.Description; row.AppSecret = r.AppSecret;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        private async Task<List<(UserSeed Record, User Row)>> UsersAsync(CancellationToken ct)
        {
            var written = new List<(UserSeed Record, User Row)>();
            var added = new List<User>();
            var changed = new List<(User Row, bool Identifiers, bool Password)>();
            foreach (var r in ExplicitFirst(seed.Users, x => x.ID))
            {
                var username = r.Username;
                var (row, write) = await MatchAsync<User>("Users", r.ID, x => x.Username == username, ct);
                var isNew = db.Entry(row).State == EntityState.Added;
                if (isNew && r.Password is null && r.PasswordHash is null)
                    throw new IdentitySeedException([$"Users ({r.Username}): a new user needs Password, or PasswordHash and Salt."]);
                if (write)
                {
                    var (oldUsername, oldEmail) = (row.Username, row.Email);
                    var branch = r.CompanyBranch is null ? null : branches[r.CompanyBranch];
                    row.Username = r.Username; row.FullName = r.FullName ?? r.Username; row.Email = r.Email; row.EmailVerified = r.EmailVerified;
                    row.Phone = r.Phone; row.PhoneVerified = r.PhoneVerified; row.BirthDate = r.BirthDate; row.IntegrationId = r.IntegrationId;
                    row.Signature = r.Signature; row.IsActive = r.IsActive; row.IsProtected = r.IsProtected;
                    row.AllowDeviceSignIn = r.AllowDeviceSignIn; row.RequireChangePassword = r.RequireChangePassword; row.AccessTree = r.AccessTree;
                    row.CompanyBranchID = branch?.ID; row.CompanyID = branch?.CompanyID; row.RegionID = branch?.RegionID; row.CountryID = branch?.CountryID;
                    var password = r.Password is not null || r.PasswordHash is not null;
                    if (r.Password is not null)
                    {
                        var hash = HashService.GenerateHash(r.Password);
                        (row.PasswordHash, row.Salt) = (hash.PasswordHash, hash.Salt);
                    }
                    else if (r.PasswordHash is not null)
                        (row.PasswordHash, row.Salt) = (r.PasswordHash, r.Salt!);
                    if (isNew) added.Add(row);
                    else changed.Add((row, !string.Equals(oldUsername, row.Username, StringComparison.Ordinal) || !string.Equals(oldEmail, row.Email, StringComparison.Ordinal), password));
                    written.Add((r, row));
                }
                users[r.Username] = row;
            }
            await db.SaveChangesAsync(ct);

            // Each new user gets its security row, so it can sign in at once, the way the authority's startup creates them.
            foreach (var user in added) db.Add(UserSecurityExpansion.CreateFor(user));
            // An updated user's security row follows the update: new identifiers get new lookup keys, and a changed
            // password or identifier ends the user's sessions.
            foreach (var (user, identifiers, password) in changed)
            {
                var userID = user.ID;
                var security = await db.Set<UserSecurityState>().SingleOrDefaultAsync(x => x.UserID == userID, ct);
                if (security is null) { db.Add(UserSecurityExpansion.CreateFor(user)); continue; }
                if (identifiers)
                {
                    var fresh = UserSecurityExpansion.CreateFor(user);
                    security.UsernameLookupKey = fresh.UsernameLookupKey;
                    security.EmailLookupKey = fresh.EmailLookupKey;
                    security.ContactRevision++;
                    RecoveryContact.Invalidate(security);
                    if (RecoveryContact.LookupMatches(user, security)) RecoveryContact.PreserveVerifiedLegacyEmail(user, security);
                }
                if (identifiers || password) security.SecurityVersion++;
            }
            await db.SaveChangesAsync(ct);

            foreach (var (r, row) in written)
            {
                var userID = row.ID;
                if (r.AccessTrees is not null)
                    await LinksAsync(db.Set<UserAccessTree>().Where(x => x.UserID == userID), x => x.AccessTreeID,
                        r.AccessTrees.Select(k => accessTrees[k].ID), id => new UserAccessTree { UserID = userID, AccessTreeID = id }, ct);
            }
            await db.SaveChangesAsync(ct);
            return written;
        }

        /// <summary>
        /// Team members come from both sides: a team's <c>Users</c> and a user's <c>Teams</c>. A written team with a
        /// <c>Users</c> list, and a written user with a <c>Teams</c> list, end up with exactly the members the seed names.
        /// </summary>
        private async Task TeamMembersAsync(List<(TeamSeed Record, Team Row)> writtenTeams, List<(UserSeed Record, User Row)> writtenUsers, CancellationToken ct)
        {
            var pairs = new HashSet<(long TeamID, long UserID)>();
            foreach (var t in seed.Teams.Where(x => x.Key is not null))
                foreach (var username in t.Users ?? [])
                    pairs.Add((teams[t.Key!].ID, users[username].ID));
            foreach (var u in seed.Users)
                foreach (var key in u.Teams ?? [])
                    pairs.Add((teams[key].ID, users[u.Username].ID));

            var managedTeams = writtenTeams.Where(x => x.Record.Users is not null).Select(x => x.Row.ID).ToHashSet();
            var managedUsers = writtenUsers.Where(x => x.Record.Teams is not null).Select(x => x.Row.ID).ToHashSet();
            var touchedTeams = pairs.Select(x => x.TeamID).Concat(managedTeams).ToHashSet();
            var existing = await db.Set<TeamUser>().IgnoreQueryFilters()
                .Where(x => touchedTeams.Contains(x.TeamID) || managedUsers.Contains(x.UserID)).ToListAsync(ct);
            foreach (var link in existing)
                if (!pairs.Remove((link.TeamID, link.UserID)) && (managedTeams.Contains(link.TeamID) || managedUsers.Contains(link.UserID)))
                    db.Remove(link);
            foreach (var (teamID, userID) in pairs) db.Add(new TeamUser { TeamID = teamID, UserID = userID });
            await db.SaveChangesAsync(ct);
        }

        private async Task CalendarsAsync(CancellationToken ct)
        {
            foreach (var r in ExplicitFirst(seed.CompanyCalendars, x => x.ID))
            {
                var companyID = r.Company is null ? (long?)null : companies[r.Company].ID;
                var title = r.Title;
                var (row, write) = await MatchAsync<CompanyCalendar>("CompanyCalendars", r.ID, x => x.Title == title && x.CompanyID == companyID, ct);
                if (!write) continue;
                row.Title = r.Title; row.CompanyID = companyID; row.StartDate = r.StartDate; row.EndDate = r.EndDate;
                row.EntryType = r.EntryType; row.Priority = r.Priority;
                row.ShiftGroups = [.. r.ShiftGroups.Select(g => new CompanyCalendarShiftGroup
                {
                    DepartmentIds = [.. g.Departments.Select(k => departments[k].ID)], BrandIds = [.. g.Brands.Select(k => brands[k].ID)],
                    Days = g.Days is null ? null : [.. g.Days], Shifts = [.. g.Shifts],
                })];
                row.WeekendGroups = [.. r.WeekendGroups.Select(g => new CompanyCalendarWeekendGroup
                {
                    DepartmentIds = [.. g.Departments.Select(k => departments[k].ID)], BrandIds = [.. g.Brands.Select(k => brands[k].ID)],
                    Weekends = [.. g.Weekends],
                })];
                await db.SaveChangesAsync(ct);
                if (r.CompanyBranches is not null)
                {
                    var calendarID = row.ID;
                    await LinksAsync(db.Set<CompanyCalendarBranch>().Where(x => x.CompanyCalendarID == calendarID), x => x.CompanyBranchID,
                        r.CompanyBranches.Select(k => branches[k].ID), id => new CompanyCalendarBranch { CompanyCalendarID = calendarID, CompanyBranchID = id }, ct);
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
