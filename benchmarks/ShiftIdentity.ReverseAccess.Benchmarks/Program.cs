using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftIdentity.ReverseAccess.Benchmarks;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Paths/settings are explicit. Never write credentials, source permission trees, or personal fields.
        try
        {
            if(args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("Required: --settings <local appsettings.json> --output <aggregate JSON outside the repository>");
                Console.WriteLine("Optional: --mode measure|inspect|sql-inspect|sql-catalogs --sql-key <connection-string key> --database <local catalog>");
                Console.WriteLine("Optional: --assemblies <absolute DLL paths separated by |> --system-action <full type name:static field>");
                Console.WriteLine("Optional: --iterations 120 --scales 1000,5000 (use 0 for no synthetic scales)");
                Console.WriteLine("Local backup: --bacpac <existing export> --dac-assembly <Microsoft.Data.Tools.Schema.Sql.dll>; reads directly, no database import.");
                Console.WriteLine("Optional: --exclude-action-root <full type name> --exclude-incomplete-scope true (explicitly reported benchmark-only subset)");
                Console.WriteLine("Memory experiment: --mode coexist; optional --hold-ms 30000 writes aggregate readiness metadata for independent process measurements.");
                Console.WriteLine("Per event: --mode candidates; optional --candidates 10,100,500 --events 100 --seed 1 (prepare and check times, reuse in one scope, Match cost).");
                Console.WriteLine("Build Release. For stable JIT comparison set DOTNET_TieredCompilation=0 before launching.");
                Console.WriteLine("measure uses SQL projections replayed in the real cache; inspect probes actual local Cosmos reads. No writes to either database.");
                return 0;
            }
            if(args.Length%2!=0)throw new ArgumentException();
            var arguments = args.Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
            var settings = Settings.Read(arguments["--settings"]);
            if (arguments.GetValueOrDefault("--mode") == "sql-catalogs")
            {
                Console.WriteLine(JsonSerializer.Serialize(await Dataset.InspectCatalogs(settings)));
                return 0;
            }
            Dataset data;object? provenance=null;
            if(arguments.TryGetValue("--bacpac",out var backup))
                (data,provenance)=BacpacInput.Read(backup,arguments["--dac-assembly"]);
            else
                data = await Dataset.ReadLocalSql(settings, arguments.GetValueOrDefault("--sql-key", "SQLServer"), arguments.GetValueOrDefault("--database"));
            var report = new Dictionary<string, object?>
            {
                ["utc"] = DateTimeOffset.UtcNow, ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = RuntimeInformation.OSDescription, ["logicalProcessors"] = Environment.ProcessorCount,
                ["serverGc"] = System.Runtime.GCSettings.IsServerGC, ["dataset"] = data.Shape(),
                ["tieredCompilationEnvironment"] = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
                ["provenance"]=provenance,
            };
            var assemblies = new List<Assembly> { typeof(ShiftIdentityActions).Assembly, typeof(ShiftEntityOptions).Assembly };
            if (arguments.TryGetValue("--assemblies", out var paths))
                assemblies.AddRange(paths.Split('|').Select(path => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path))));
            var actions = assemblies.SelectMany(a => a.GetExportedTypes())
                .Where(t => !t.IsNested && t.GetCustomAttribute<ActionTree>() is not null).Distinct().ToArray();
            if(arguments.TryGetValue("--exclude-action-root",out var excludedRoot))actions=actions.Where(t=>t.FullName!=excludedRoot).ToArray();
            report["registeredActionTrees"] = actions.Length;
            if(arguments.GetValueOrDefault("--exclude-incomplete-scope")=="true")
            {
                var excluded=data.Users.RemoveAll(u=>u.IsActive==true&&!u.IsDeleted&&data.HasIncompleteScope(u));
                report["benchmarkScope"]=new {ExcludedIncompleteActiveUsers=excluded,Reason="Explicit benchmark-only subset. The resolver gives these users a NotEligible or Unresolved outcome, not a subject; no source data was repaired.",Dataset=data.Shape()};
            }
            if (arguments.GetValueOrDefault("--mode") == "sql-inspect") { }
            else if (arguments.GetValueOrDefault("--mode") == "inspect")
                report["cosmos"] = await CosmosProbe.Run(settings);
            else if(arguments.GetValueOrDefault("--mode")=="coexist")
                report["coexistence"]=await Coexistence.Run(data,settings.HashIds(),actions,arguments);
            else if(arguments.GetValueOrDefault("--mode")=="candidates")
                report["candidates"]=await Candidates.Run(data,settings.HashIds(),actions,arguments);
            else
                report["measurements"] = await Measurements.Run(data, settings.HashIds(), actions, arguments);
            await File.WriteAllTextAsync(arguments["--output"], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Completed. Aggregate results saved.");
            return 0;
        }
        catch (Exception ex)
        {
            // SQL/Cosmos exception messages can contain host names, document values, or credentials.
            Console.Error.WriteLine("Benchmark failed: " + ex.GetType().Name);
            if (ex is Microsoft.Data.SqlClient.SqlException sql) Console.Error.WriteLine("SQL error number: " + sql.Number);
            if (ex is Microsoft.Azure.Cosmos.CosmosException cosmos) Console.Error.WriteLine("Cosmos status: " + (int)cosmos.StatusCode);
            return 1;
        }
    }
}
