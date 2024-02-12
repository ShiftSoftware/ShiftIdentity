using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Entities;
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
        public DbSet<UserAccessTree> UserAccessTrees { get; set; }

        public DbSet<Company> Companies { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<CompanyBranch> CompanyBranches { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<CompanyBranchDepartment> CompanyBranchDepartments { get; set; }
        public DbSet<CompanyBranchService> CompanyBranchServices { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<UserGroupUser> UserGroupUsers { get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<AccessTree>().HasIndex(x => x.Name).IsUnique().HasFilter($"{nameof(AccessTree.IsDeleted)} = 0");
            b.Entity<App>().HasIndex(x => x.AppId).IsUnique().HasFilter($"{nameof(App.IsDeleted)} = 0");
            b.Entity<User>().HasIndex(x => x.Username).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0");
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
            });
        }
    }
}
