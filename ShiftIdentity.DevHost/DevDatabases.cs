using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;

namespace ShiftIdentity.DevHost;

/// <summary>
/// A stopped DevHost drops its database, but a killed one cannot: Visual Studio's Stop Debugging and a closed console
/// window end the process at once. Each run therefore marks its database with its process, and the next run drops
/// the databases whose process is gone. Databases of the SQL tests carry no such mark and are never touched.
/// </summary>
internal static class DevDatabases
{
    private static readonly Regex OwnedName = new("^ShiftIdentityTests_[a-f0-9]{32}$", RegexOptions.CultureInvariant);

    public static async Task MarkAsync(SqlIdentityFixture fixture)
    {
        using var self = Process.GetCurrentProcess();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [dbo].[DevHostRun] ([ProcessId] int NOT NULL, [ProcessStartedAt] datetime2 NOT NULL); " +
            "INSERT INTO [dbo].[DevHostRun] VALUES ({0}, {1})", self.Id, self.StartTime.ToUniversalTime());
    }

    /// <summary>Drops the databases of earlier DevHost runs on the same SQL Server whose process has ended.</summary>
    public static async Task<int> DropAbandonedAsync(SqlIdentityFixture fixture)
    {
        string current;
        await using (var db = fixture.CreateContext()) current = db.Database.GetConnectionString()!;
        var builder = new SqlConnectionStringBuilder(current);
        var own = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        await using var server = new SqlConnection(builder.ConnectionString);
        await server.OpenAsync();

        var names = new List<string>();
        await using (var list = server.CreateCommand())
        {
            list.CommandText = "SELECT [name] FROM sys.databases WHERE [name] LIKE N'ShiftIdentityTests[_]%' AND state_desc = N'ONLINE'";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        }

        var dropped = 0;
        foreach (var name in names.Where(x => x != own && OwnedName.IsMatch(x)))
        {
            try
            {
                await using var read = server.CreateCommand();
                read.CommandText = $"IF OBJECT_ID(N'[{name}].[dbo].[DevHostRun]') IS NOT NULL AND OBJECT_ID(N'[{name}].[dbo].[IdentityTestOwnership]') IS NOT NULL " +
                    $"SELECT TOP (1) [ProcessId], [ProcessStartedAt] FROM [{name}].[dbo].[DevHostRun]";
                int? process = null; DateTime startedAt = default;
                await using (var reader = await read.ExecuteReaderAsync())
                    if (await reader.ReadAsync()) { process = reader.GetInt32(0); startedAt = reader.GetDateTime(1); }
                if (process is null || StillRunning(process.Value, startedAt)) continue;
                await using var drop = server.CreateCommand();
                drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
                await drop.ExecuteNonQueryAsync();
                dropped++;
            }
            // A database that disappears or is being created meanwhile belongs to someone else's run: leave it.
            catch (SqlException) { }
        }
        return dropped;
    }

    private static bool StillRunning(int id, DateTime startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return Math.Abs((process.StartTime.ToUniversalTime() - DateTime.SpecifyKind(startedAt, DateTimeKind.Utc)).TotalSeconds) < 2;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // A process that cannot be read is another user's: leave its database alone.
        catch (Win32Exception) { return true; }
    }
}
