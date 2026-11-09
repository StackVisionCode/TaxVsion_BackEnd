using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Base temporal con el modelo de Auth, en el SQL Server local. Se borra al terminar.</summary>
public sealed class AuthTestDatabase : IAsyncDisposable
{
    private AuthTestDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    public static async Task<AuthTestDatabase> CreateAsync()
    {
        var local =
            Environment.GetEnvironmentVariable("AUTH_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set AUTH_DB_CONNECTION or the Auth user-secrets to run this test.");

        var builder = new SqlConnectionStringBuilder(local)
        {
            InitialCatalog = $"TaxVision_AuthTest_{Guid.NewGuid():N}",
        };
        var database = new AuthTestDatabase(builder.ConnectionString);
        await using var db = database.NewContext(Guid.Empty);
        await db.Database.EnsureCreatedAsync();
        return database;
    }

    public AuthDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
