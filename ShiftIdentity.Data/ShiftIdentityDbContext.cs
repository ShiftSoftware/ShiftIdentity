using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data.Triggers;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Data
{
    public abstract class ShiftIdentityDbContext : ShiftDbContext
    {
        public ShiftIdentityDbContext()
        {

        }

        public ShiftIdentityDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<App> Apps { get; set; }
        public DbSet<AccessTree> AccessTrees { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserLog> UserLogs { get; set; }
        public DbSet<UserAccessTree> UserAccessTrees { get; set; }

        public DbSet<Company> Companies { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<CompanyBranch> CompanyBranches { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<CompanyBranchDepartment> CompanyBranchDepartments { get; set; }
        public DbSet<CompanyBranchService> CompanyBranchServices { get; set; }
        public DbSet<CompanyBranchBrand> CompanyBranchBrands { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<TeamUser> TeamUsers { get; set; }
        public DbSet<TeamCompanyBranch> TeamCompanyBranches { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<CompanyCalendar> CompanyCalendars { get; set; }
        public DbSet<CompanyCalendarBranch> CompanyCalendarBranches { get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<AccessTree>().HasIndex(x => x.Name).IsUnique().HasFilter($"{nameof(AccessTree.IsDeleted)} = 0");

            b.Entity<App>().HasIndex(x => x.AppId).IsUnique().HasFilter($"{nameof(App.IsDeleted)} = 0");

            b.Entity<User>(x =>
            {
                x.HasIndex(x => x.Username).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0");
                x.HasIndex(x => x.IntegrationId).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0 AND {nameof(User.IntegrationId)} is not null");
                x.HasIndex(x => x.Email).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0 AND {nameof(User.Email)} is not null");
                x.HasIndex(x => x.Phone).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0 AND {nameof(User.Phone)} is not null");
            });

            b.Entity<Brand>(x =>
            {
                x.Property(c => c.BrandID).HasComputedColumnSql(nameof(Brand.ID));
            });

            b.Entity<Team>(x =>
            {
                x.Property(p => p.Tags).HasConversion(
                    x => JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    x => string.IsNullOrWhiteSpace(x) ? new() : JsonSerializer.Deserialize<List<string>>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                );

                x.Property(c => c.TeamID).HasComputedColumnSql(nameof(Team.ID));
            });

            b.Entity<CompanyBranch>(x =>
            {
                x.Property(p => p.CustomFields).HasConversion(
                    x => JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    x => JsonSerializer.Deserialize<Dictionary<string, CustomField>>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    new ValueComparer<Dictionary<string, CustomField>>(
                        (c1, c2) => c1.SequenceEqual(c2),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToDictionary(x => x.Key, x => x.Value)
                    )
                );

                x.Property(p => p.Phones).HasConversion(
                    x => JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    x => string.IsNullOrWhiteSpace(x) ? new () : JsonSerializer.Deserialize<List<TaggedTextDTO>>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                );

                x.Property(p => p.Emails).HasConversion(
                    x => JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    x => string.IsNullOrWhiteSpace(x) ? new() : JsonSerializer.Deserialize<List<TaggedTextDTO>>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                );

                x.Property(c => c.CompanyBranchID).HasComputedColumnSql(nameof(CompanyBranch.ID));
                x.HasIndex(c => c.DisplayOrder);
            });

            b.Entity<Company>(x =>
            {
                x.Property(p => p.CustomFields).HasConversion(
                    x => JsonSerializer.Serialize(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    x => JsonSerializer.Deserialize<Dictionary<string, CustomField>>(x, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    new ValueComparer<Dictionary<string, CustomField>>(
                        (c1, c2) => c1.SequenceEqual(c2),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToDictionary(x => x.Key, x => x.Value)
                    )
                );

                x.HasOne(x => x.ParentCompany)
                    .WithMany(x => x.ChildCompanies)
                    .HasForeignKey(x => x.ParentCompanyID)
                    .OnDelete(DeleteBehavior.Restrict);

                x.Property(c => c.CompanyID).HasComputedColumnSql(nameof(Company.ID));
                x.HasIndex(c => c.DisplayOrder);
            });

            b.Entity<City>(x =>
            {
                x.Property(c => c.CityID).HasComputedColumnSql(nameof(City.ID));
                x.HasIndex(c => c.DisplayOrder);
            });

            b.Entity<Country>(x =>
            {
                x.Property(c => c.CountryID).HasComputedColumnSql(nameof(Country.ID));
                x.HasIndex(c => c.DisplayOrder);
            });

            b.Entity<Region>(x =>
            {
                x.Property(c => c.RegionID).HasComputedColumnSql(nameof(Region.ID));
                x.HasIndex(c => c.DisplayOrder);
            });

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

            var shiftGroupsConverter = new ValueConverter<List<CompanyCalendarShiftGroup>, string>(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<CompanyCalendarShiftGroup>>(v, jsonOptions) ?? new List<CompanyCalendarShiftGroup>()
            );

            var weekendGroupsConverter = new ValueConverter<List<CompanyCalendarWeekendGroup>, string>(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<CompanyCalendarWeekendGroup>>(v, jsonOptions) ?? new List<CompanyCalendarWeekendGroup>()
            );

            b.Entity<CompanyCalendar>(x =>
            {
                x.Property(d => d.ShiftGroups)
                    .HasConversion(shiftGroupsConverter)
                    .HasColumnType("nvarchar(max)");

                x.Property(d => d.WeekendGroups)
                    .HasConversion(weekendGroupsConverter)
                    .HasColumnType("nvarchar(max)");

                x.HasMany(d => d.Branches)
                    .WithOne(cb => cb.CompanyCalendar)
                    .HasForeignKey(cb => cb.CompanyCalendarID)
                    .OnDelete(DeleteBehavior.Cascade);

                x.HasIndex(d => new { d.StartDate, d.EndDate, d.CompanyID, d.IsDeleted });
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseTriggers(x=> x.AddTrigger<ResetUserTrigger>());

            base.OnConfiguring(optionsBuilder);
        }
    }
}
