using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftIdentity.Dashboard.AspNetCore.Data
{
    public class DBSeed
    {
        private ShiftIdentityDB db;

        private List<Type> actionTrees;
        private readonly string superUserPassword;

        private long builtInUserId { get; set; } = 1;

        public DBSeed(ShiftIdentityDB db, List<Type> actionTrees, string superUserPassword)
        {
            this.db = db;
            this.actionTrees = actionTrees;
            this.superUserPassword = superUserPassword;
        }

        public async Task SeedAsync()
        {
            await SeedUserAsync();

            await db.SaveChangesAsync();
        }

        private async Task SeedUserAsync()
        {
            var user = await db.Users.FindAsync(builtInUserId);

            var tree = new Dictionary<string, object>();

            foreach (var item in actionTrees)
            {
                tree[item.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };
            }

            var jsonTree = System.Text.Json.JsonSerializer.Serialize(tree);

            if (user == null)
            {
                user = new User
                {
                    FullName = "Super User",
                    IsActive = true,
                    Username = "SuperUser",
                    BuiltIn = true,
                    RequireChangePassword = false
                }.CreateShiftEntity();

                db.Users.Add(user);
            }

            user.AccessTree = jsonTree;

            var hash = HashService.GenerateHash(superUserPassword);

            user.PasswordHash = hash.PasswordHash;

            user.Salt = hash.Salt;
        }
    }
}
