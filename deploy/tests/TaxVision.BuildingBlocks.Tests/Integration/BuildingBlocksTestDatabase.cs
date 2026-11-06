using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// SQL Server local para las T-INT de BuildingBlocks: <c>BUILDINGBLOCKS_DB_CONNECTION</c> o los user-secrets
/// de Tasks.Api (el piloto de la Ola 2). Cada prueba trabaja en un esquema propio y lo borra entero al terminar.
/// </summary>
internal static class BuildingBlocksTestDatabase
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("BUILDINGBLOCKS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set BUILDINGBLOCKS_DB_CONNECTION or the Tasks.Api user-secrets to run this test."
        );

    public static string NewSchema(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    public static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>Cuenta filas si la tabla existe; 0 si todavía no se creó.</summary>
    public static Task<int> CountRowsAsync(string schema, string table, string where = "1 = 1") =>
        CountAsync($"IF OBJECT_ID('[{schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{schema}].[{table}] WHERE {where}");

    /// <summary>Borra el esquema de prueba con todo lo que tenga (FK, tablas, vistas, procedimientos y tipos).</summary>
    public static Task DropSchemaAsync(string schema) =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP VIEW ' + QUOTENAME(s.name) + N'.' + QUOTENAME(v.name) + N';'
              FROM sys.views v JOIN sys.schemas s ON v.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.objects o JOIN sys.schemas s ON o.schema_id = s.schema_id
             WHERE s.name = N'{schema}' AND o.type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(q.name) + N';'
              FROM sys.sequences q JOIN sys.schemas s ON q.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );
}
