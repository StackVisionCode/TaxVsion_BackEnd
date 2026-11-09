using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>Base temporal con el esquema de Scribe. Se borra al arrancar, por si una corrida abortada la dejó,
/// y al terminar: los datos locales no se tocan.</summary>
public sealed class ScribeTempDatabase : IAsyncLifetime
{
    private const string DatabaseName = "scribe_o3_concurrency_it";

    public static string Connection { get; } =
        new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("SCRIBE_DB_CONNECTION")
                ?? new ConfigurationBuilder()
                    .AddUserSecrets("44915234-5957-4298-a439-621831933142")
                    .Build()
                    .GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Set SCRIBE_DB_CONNECTION or the Scribe user-secrets to run this test."
                )
        )
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    public ScribeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ScribeDbContext>().UseSqlServer(Connection).Options, new NoTenantContext());

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

/// <summary>Una sola base para todas las clases: en paralelo se borrarían entre ellas.</summary>
[CollectionDefinition(Name)]
public sealed class ScribeTempDatabaseCollection : ICollectionFixture<ScribeTempDatabase>
{
    public const string Name = "ScribeTempDatabase";
}
