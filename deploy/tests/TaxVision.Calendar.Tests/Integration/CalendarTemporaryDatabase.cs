using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using TaxVision.Calendar.Infrastructure.Persistence;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Base temporal propia de una T-INT: se crea desde el modelo y se borra al terminar. La flota nunca ve estas filas
/// ni estos sobres, aunque la prueba se aborte.
/// </summary>
internal sealed class CalendarTemporaryDatabase
{
    private const string ApiUserSecretsId = "b7f4a1d2-9c3e-4a58-8f21-6d0e5b3c74a9";
    private const string Prefix = "calint_";

    public CalendarTemporaryDatabase()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("CALENDAR_DB_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set CALENDAR_DB_CONNECTION or the Calendar user-secrets.");
        ConnectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    public async Task CreateAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DropAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    public CalendarDbContext NewContext(params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<CalendarDbContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(interceptors)
                .Options,
            new NoTenantContext()
        );

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    internal sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
