using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ShiftIdentity.Tests.Infrastructure;

/// <summary>
/// An empty catalog that a test creates on the local SQL Server and drops afterwards, for tests that need SQL Server
/// itself rather than SQLite. It refuses any server that is not local, and it drops only a catalog that still holds
/// the ownership marker it wrote. Set <c>SHIFT_IDENTITY_TEST_SQL</c> to use another local server.
/// </summary>
public sealed class OwnedSqlCatalog : IAsyncDisposable
{
    private readonly string serverConnectionString;
    private readonly Guid ownership = Guid.NewGuid();

    public string Name { get; }

    /// <summary>The connection string of the new catalog.</summary>
    public string ConnectionString { get; }

    private OwnedSqlCatalog(string prefix)
    {
        Name = prefix + "_" + Guid.NewGuid().ToString("N");

        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SHIFT_IDENTITY_TEST_SQL") ??
            @"Server=.\SQLEXPRESS;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=5");

        // A test override supplies local connection details, never a remote application database.
        if (!Regex.IsMatch(builder.DataSource, @"^(?:\.\\SQLEXPRESS|localhost(?:,\d+)?|127\.0\.0\.1(?:,\d+)?)$", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("SQL Server tests require a local disposable SQL Server.");
        if (!string.IsNullOrEmpty(builder.InitialCatalog) && builder.InitialCatalog != "master")
            throw new InvalidOperationException("Do not supply an existing database to SQL Server tests.");

        builder.InitialCatalog = "master";
        serverConnectionString = builder.ConnectionString;
        builder.InitialCatalog = Name;
        ConnectionString = builder.ConnectionString;
    }

    /// <summary>Creates the catalog and writes the ownership marker. Its name starts with <paramref name="prefix"/>.</summary>
    public static async Task<OwnedSqlCatalog> CreateAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var catalog = new OwnedSqlCatalog(prefix);

        await using (var server = new SqlConnection(catalog.serverConnectionString))
        {
            await server.OpenAsync(cancellationToken);
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{catalog.Name}]";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var owned = new SqlConnection(catalog.ConnectionString))
        {
            await owned.OpenAsync(cancellationToken);
            await using var mark = owned.CreateCommand();
            mark.CommandText = "CREATE TABLE [dbo].[TestOwnership] ([Marker] uniqueidentifier NOT NULL PRIMARY KEY); " +
                "INSERT INTO [dbo].[TestOwnership] VALUES (@marker)";
            mark.Parameters.AddWithValue("@marker", catalog.ownership);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        return catalog;
    }

    public async ValueTask DisposeAsync()
    {
        await using (var owned = new SqlConnection(ConnectionString))
        {
            await owned.OpenAsync();
            await using var check = owned.CreateCommand();
            check.CommandText = "SELECT [Marker] FROM [dbo].[TestOwnership]";
            if (await check.ExecuteScalarAsync() is not Guid marker || marker != ownership)
                throw new InvalidOperationException("Database ownership marker mismatch; refusing teardown.");
        }

        // Only this catalog's pooled connections must go before the drop.
        SqlConnection.ClearPool(new SqlConnection(ConnectionString));
        await using var server = new SqlConnection(serverConnectionString);
        await server.OpenAsync();
        await using var drop = server.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]";
        await drop.ExecuteNonQueryAsync();
    }
}
