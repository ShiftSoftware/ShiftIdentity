﻿using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data
{
    public class ShiftIdentityDB : ShiftDbContext
    {
        public ShiftIdentityDB()
        {

        }

        public ShiftIdentityDB(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<App> Apps { get; set; }
        public DbSet<AccessTree> AccessTrees { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserAccessTree> UserAccessTrees { get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<AccessTree>().HasIndex(x => x.Name).IsUnique().HasFilter($"{nameof(AccessTree.IsDeleted)} = 0");
            b.Entity<App>().HasIndex(x => x.AppId).IsUnique().HasFilter($"{nameof(App.IsDeleted)} = 0");
            b.Entity<User>().HasIndex(x => x.Username).IsUnique().HasFilter($"{nameof(User.IsDeleted)} = 0");
        }
    }
}