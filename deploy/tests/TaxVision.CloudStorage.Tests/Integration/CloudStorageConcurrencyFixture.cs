using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class CloudStorageConcurrencyCollection : ICollectionFixture<CloudStorageConcurrencyFixture>
{
    public const string Name = "CloudStorage concurrency database";
}

/// <summary>
/// Base temporal con las migraciones reales: las pruebas de concurrencia no tocan la base de la flota.
/// Se borra al arrancar, por si una corrida abortada la dejó, y al terminar.
/// </summary>
public sealed class CloudStorageConcurrencyFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = TemporaryDatabase.ConnectionStringFor("cst_ola3_concurrency_it");

    public CloudStorageDbContext NewContext(Guid tenantId) => TemporaryDatabase.NewContext(ConnectionString, tenantId);

    public async Task InitializeAsync()
    {
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext(Guid.Empty);
        await db.Database.EnsureDeletedAsync();
    }
}

internal static class TemporaryDatabase
{
    public static string ConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
                ?? new ConfigurationBuilder()
                    .AddUserSecrets("taxvision-cloudstorage-development")
                    .Build()
                    .GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Set CLOUDSTORAGE_DB_CONNECTION or the CloudStorage user-secrets to run this test."
                )
        )
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    public static CloudStorageDbContext NewContext(string connectionString, Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(connectionString).Options,
            new FixedTenant(tenantId)
        );

    public sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
