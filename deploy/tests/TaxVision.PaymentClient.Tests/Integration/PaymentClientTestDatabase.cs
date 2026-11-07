using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.PaymentClient.Infrastructure.Persistence;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>Base local de PaymentClient para las T-INT: PAYMENTCLIENT_DB_CONNECTION o los user-secrets de la Api.</summary>
internal static class PaymentClientTestDatabase
{
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("PAYMENTCLIENT_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("7c2a4f19-3e6b-4d02-8a91-paymentclient1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set PAYMENTCLIENT_DB_CONNECTION or the PaymentClient user-secrets to run this test."
        );

    public static PaymentClientDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

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

    /// <summary>Borra las tablas de Wolverine del esquema de prueba (primero las FK) y el esquema.</summary>
    public static Task DropSchemaAsync(string schema) =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );

    public sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
