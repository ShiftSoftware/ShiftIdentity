using Microsoft.EntityFrameworkCore.Migrations;

namespace ShiftSoftware.ShiftIdentity.Data.Migrations;

// Converts ShiftIdentity's legacy nvarchar localized-text columns to json-compatible values
// ({"en":"..."}) ahead of an ALTER ... json. Covers current rows and temporal-history rows.
// Idempotent: only non-JSON, non-null values are rewritten.
public static class LocalizedJsonColumnMigration
{
    // Mirrors the [Column(TypeName = "json")] mappings on the ShiftIdentity entities.
    private static readonly (string Schema, string Table, string Column)[] ShiftIdentityColumns =
    {
        ("ShiftIdentity", "Countries",       "Name"),
        ("ShiftIdentity", "Regions",         "Name"),
        ("ShiftIdentity", "Cities",          "Name"),
        ("ShiftIdentity", "Companies",       "Name"),
        ("ShiftIdentity", "Companies",       "LegalName"),
        ("ShiftIdentity", "Companies",       "HQAddress"),
        ("ShiftIdentity", "CompanyBranches", "Name"),
        ("ShiftIdentity", "Brands",          "Name"),
        ("ShiftIdentity", "Departments",     "Name"),
        ("ShiftIdentity", "Services",        "Name"),
    };

    public static void NormalizeShiftIdentityLocalizedColumnsToJson(this MigrationBuilder migrationBuilder, string language = "en")
    {
        foreach (var (schema, table, column) in ShiftIdentityColumns)
            migrationBuilder.NormalizeColumnToLocalizedJson(schema, table, column, language);
    }

    // Rewrites non-JSON values in a table's current rows as {"<language>":"<value>"}, and by default its
    // temporal-history rows too. Runs while the column is still nvarchar.
    public static void NormalizeColumnToLocalizedJson(this MigrationBuilder migrationBuilder, string schema, string table, string column, string language = "en", bool includeHistory = true)
    {
        migrationBuilder.Sql(
            $"UPDATE [{schema}].[{table}] " +
            $"SET [{column}] = CONCAT(N'{{\"{language}\":\"', STRING_ESCAPE([{column}], 'json'), N'\"}}') " +
            $"WHERE [{column}] IS NOT NULL AND ISJSON([{column}]) = 0;");

        if (includeHistory)
            migrationBuilder.NormalizeTemporalHistoryColumnToLocalizedJson(schema, table, column, language);
    }

    // Rewrites non-JSON history values for a temporal table ("<table>History"). History is
    // read-only under system versioning, so versioning is disabled and re-enabled around the update.
    // Non-temporal tables are skipped. The update runs via EXEC so it compiles after versioning is
    // disabled; a same-batch UPDATE would bind against the still-versioned table and be rejected.
    public static void NormalizeTemporalHistoryColumnToLocalizedJson(this MigrationBuilder migrationBuilder, string schema, string table, string column, string language = "en")
    {
        var historyTable = table + "History";

        var update =
            $"UPDATE [{schema}].[{historyTable}] " +
            $"SET [{column}] = CONCAT(N'{{\"{language}\":\"', STRING_ESCAPE([{column}], 'json'), N'\"}}') " +
            $"WHERE [{column}] IS NOT NULL AND ISJSON([{column}]) = 0;";

        migrationBuilder.Sql(
            $"IF (SELECT temporal_type FROM sys.tables WHERE object_id = OBJECT_ID(N'[{schema}].[{table}]')) = 2\n" +
            "BEGIN\n" +
            $"    ALTER TABLE [{schema}].[{table}] SET (SYSTEM_VERSIONING = OFF);\n" +
            $"    EXEC(N'{update.Replace("'", "''")}');\n" +
            $"    ALTER TABLE [{schema}].[{table}] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [{schema}].[{historyTable}], DATA_CONSISTENCY_CHECK = ON));\n" +
            "END");
    }
}
