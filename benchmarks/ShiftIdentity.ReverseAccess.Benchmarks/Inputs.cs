using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal sealed class Settings(JsonNode root)
{
    public static Settings Read(string path) => new(JsonNode.Parse(File.ReadAllText(path), documentOptions:
        new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!);
    public string? Value(string path)
    {
        if (root[path] is { } flat) return flat.ToString();
        JsonNode? node = root;
        foreach (var part in path.Split(':')) node = node?[part];
        return node?.ToString();
    }
    public IHashIdService HashIds()
    {
        var options = new ShiftEntityOptions();
        options.HashId.RegisterIdentityHashId(Value("Settings:HashIdSettings:Salt") ?? "",
            int.Parse(Value("Settings:HashIdSettings:MinHashLength") ?? "0"),
            acceptUnencodedIds: bool.Parse(Value("Settings:HashIdSettings:AcceptUnencodedIds") ?? "false"));
        return new HashIdService(Options.Create(options));
    }
}

internal sealed class Dataset
{
    public static async Task<object> InspectCatalogs(Settings settings)
    {
        var builder = new SqlConnectionStringBuilder(settings.Value("ConnectionStrings:SQLServer"));
        var server = builder.DataSource.ToLowerInvariant();
        RequireLocalServer(server);
        builder.InitialCatalog = "master";
        builder.ConnectTimeout = 10;
        await using var db = new SqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        await using var command = new SqlCommand("SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1", db);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }
    public List<UserModel> Users { get; } = [];
    public List<AccessTreeModel> Trees { get; } = [];
    public List<UserAccessTreeModel> Assignments { get; } = [];
    public List<TeamUserModel> Memberships { get; } = [];
    public List<CompanyModel> Companies { get; } = [];
    public List<CompanyBranchModel> Branches { get; } = [];

    // Only the required scope and grant columns are selected. Names and contacts never leave SQL.
    public static async Task<Dataset> ReadLocalSql(Settings settings, string connectionKey = "SQLServer", string? catalog = null)
    {
        var builder = new SqlConnectionStringBuilder(settings.Value("ConnectionStrings:" + connectionKey));
        if (catalog is not null) builder.InitialCatalog = catalog;
        var server = builder.DataSource.ToLowerInvariant();
        RequireLocalServer(server);
        builder.ApplicationIntent=ApplicationIntent.ReadOnly;
        builder.ConnectTimeout = 10;
        builder.ApplicationName = "Read-only reverse access benchmark";
        var data = new Dataset();
        await using var db = new SqlConnection(builder.ConnectionString);
        await db.OpenAsync();
        async Task Read(string sql, Action<SqlDataReader> add)
        {
            await using var command = new SqlCommand(sql, db) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) add(reader);
        }
        static long? Id(SqlDataReader r, int n) => r.IsDBNull(n) ? null : Convert.ToInt64(r.GetValue(n));
        await Read("SELECT ID,IsActive,IsDeleted,AccessTree,CompanyID,CompanyBranchID,RegionID,CountryID FROM ShiftIdentity.Users", r =>
            data.Users.Add(new() { id = r.GetInt64(0).ToString(CultureInfo.InvariantCulture), IsActive = r.GetBoolean(1),
                IsDeleted = r.GetBoolean(2), AccessTree = r.IsDBNull(3) ? null : r.GetString(3), CompanyID = Id(r,4),
                CompanyBranchID = Id(r,5), RegionID = Id(r,6), CountryID = Id(r,7), Username = "benchmark", FullName = "benchmark" }));
        await Read("SELECT ID,Tree,IsDeleted FROM ShiftIdentity.AccessTrees", r => data.Trees.Add(new()
            { id = r.GetInt64(0).ToString(), Tree = r.GetString(1), IsDeleted = r.GetBoolean(2), Name = "benchmark" }));
        await Read("SELECT ID,UserID,AccessTreeID,IsDeleted FROM ShiftIdentity.UserAccessTrees", r => data.Assignments.Add(new()
            { id = r.GetInt64(0).ToString(), UserID = r.GetInt64(1), AccessTreeID = r.GetInt64(2), IsDeleted = r.GetBoolean(3) }));
        await Read("SELECT ID,UserID,TeamID,IsDeleted FROM ShiftIdentity.TeamUsers", r => data.Memberships.Add(new()
            { id = r.GetInt64(0).ToString(), UserID = r.GetInt64(1), TeamID = r.GetInt64(2), IsDeleted = r.GetBoolean(3) }));
        await Read("SELECT ID,CompanyType,IsDeleted FROM ShiftIdentity.Companies", r => data.Companies.Add(new()
            { id = r.GetInt64(0).ToString(), CompanyType = (CompanyTypes)Convert.ToInt32(r.GetValue(1)), IsDeleted = r.GetBoolean(2), Name = "benchmark" }));
        await Read("SELECT ID,CityID,IsDeleted FROM ShiftIdentity.CompanyBranches", r => data.Branches.Add(new()
            { id = r.GetInt64(0).ToString(), CityID = Id(r,1), IsDeleted = r.GetBoolean(2), Name = "benchmark" }));
        return data;
    }

    private static void RequireLocalServer(string server)
    {
        var host=server.Split('\\',',')[0];
        if(host is not "." and not "localhost" and not "127.0.0.1" and not "(localdb)" && host!=Environment.MachineName.ToLowerInvariant())
            throw new InvalidOperationException("The benchmark SQL reader only permits a local server.");
    }

    public bool HasIncompleteScope(UserModel u) => ScopeIssues(u).Any();
    private IEnumerable<string> ScopeIssues(UserModel u)
    {
        if(u.CompanyID is null)yield return "missing-user-company";
        else if(!Companies.Any(c=>c.id==u.CompanyID.ToString()))yield return "missing-company-document";
        if(u.RegionID is null)yield return "missing-user-region";
        if(u.CompanyBranchID is null)yield return "missing-user-branch";
        else
        {
            var branch=Branches.SingleOrDefault(b=>b.id==u.CompanyBranchID.ToString());
            if(branch is null)yield return "missing-branch-document";
            else if(branch.CityID is null)yield return "missing-branch-city";
        }
    }

    public object Shape()
    {
        var assignmentsPerUser=Assignments.GroupBy(a=>a.UserID.ToString()).ToDictionary(g=>g.Key,g=>g.Count());
        var membershipsPerUser=Memberships.GroupBy(a=>a.UserID.ToString()).ToDictionary(g=>g.Key,g=>g.Count());
        static int Depth(string? json)
        {
            if(string.IsNullOrWhiteSpace(json)) return 0;
            using var document=System.Text.Json.JsonDocument.Parse(json);
            static int Walk(System.Text.Json.JsonElement element) => element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object => 1+element.EnumerateObject().Select(p=>Walk(p.Value)).DefaultIfEmpty().Max(),
                System.Text.Json.JsonValueKind.Array => 1+element.EnumerateArray().Select(Walk).DefaultIfEmpty().Max(),
                _=>0,
            };
            return Walk(document.RootElement);
        }
        static object Distribution(IEnumerable<int> input)
        {
            var a = input.Order().ToArray();
            return new { Count = a.Length, Min = a.FirstOrDefault(), Median = a.ElementAtOrDefault(a.Length/2),
                P95 = a.ElementAtOrDefault(Math.Max(0,(int)Math.Ceiling(a.Length*.95)-1)), Max = a.LastOrDefault(), Total = a.Sum() };
        }
        return new { Users = Users.Count, ActiveUsers = Users.Count(u => u.IsActive == true && !u.IsDeleted),
            ExcludedDeletedUsers=Users.Count(u=>u.IsDeleted),ExcludedInactiveNotDeletedUsers=Users.Count(u=>!u.IsDeleted&&u.IsActive!=true),
            ActiveWithMissingScope=Users.Count(u=>u.IsActive==true&&!u.IsDeleted&&(u.CompanyID is null||u.CompanyBranchID is null||u.RegionID is null||
                !Companies.Any(c=>c.id==u.CompanyID.ToString())||!Branches.Any(b=>b.id==u.CompanyBranchID.ToString()&&b.CityID.HasValue))),
            ActiveScopeIssues=Users.Where(u=>u.IsActive==true&&!u.IsDeleted).SelectMany(ScopeIssues).GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count()),
            ActiveAssignments=Assignments.Count(a=>Users.Any(u=>u.id==a.UserID.ToString()&&u.IsActive==true&&!u.IsDeleted)),
            NamedTrees = Trees.Count, Assignments = Assignments.Count, Memberships = Memberships.Count,
            Teams = Memberships.Select(x => x.TeamID).Distinct().Count(), Companies = Companies.Count, Branches = Branches.Count,
            DirectTreeBytes = Distribution(Users.Select(u => System.Text.Encoding.UTF8.GetByteCount(u.AccessTree ?? ""))),
            NamedTreeBytes = Distribution(Trees.Select(t => System.Text.Encoding.UTF8.GetByteCount(t.Tree))),
            DirectTreeDepth = Distribution(Users.Select(u => Depth(u.AccessTree))),
            NamedTreeDepth = Distribution(Trees.Select(t => Depth(t.Tree))),
            AssignmentsPerUser = Distribution(Users.Select(u => assignmentsPerUser.GetValueOrDefault(u.id))),
            MembershipsPerUser = Distribution(Users.Select(u => membershipsPerUser.GetValueOrDefault(u.id))) };
    }
}

internal sealed class MemorySource : CosmosIdentityReferenceSource<MemoryClient>
{
    private readonly Dictionary<Type, IEnumerable<ReplicationModel>> rows;
    public int Reads;
    private readonly bool cloneRows;
    public MemorySource(Dataset data, IdentityReferenceCache? cache = null,bool cloneRows=false)
        : base(new MemoryClient(), Options.Create(new CosmosIdentityReferenceOptions()), cache)
    {
        this.cloneRows=cloneRows;
        rows = new() { [typeof(UserModel)] = data.Users, [typeof(AccessTreeModel)] = data.Trees,
            [typeof(UserAccessTreeModel)] = data.Assignments, [typeof(TeamUserModel)] = data.Memberships,
            [typeof(CompanyModel)] = data.Companies, [typeof(CompanyBranchModel)] = data.Branches };
    }
    internal override Task<IReadOnlyList<T>> ReadRowsAsync<T>(string containerName, string? itemType, string? id, CancellationToken ct)
    {
        Interlocked.Increment(ref Reads);
        IReadOnlyList<T> values = rows[typeof(T)].Cast<T>().Where(x => id is null || x.id == id).ToArray();
        if(cloneRows)values=System.Text.Json.JsonSerializer.Deserialize<T[]>(System.Text.Json.JsonSerializer.Serialize(values))!;
        return Task.FromResult(values);
    }
}
internal sealed class MemoryClient : CosmosClient;
