extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de CloudStorage (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class CloudStorageIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "cst_tint",
        () =>
            Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("taxvision-cloudstorage-development")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set CLOUDSTORAGE_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new CloudStorageDbContext(
                new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(connection).Options,
                new NoTenant()
            );
            db.Database.Migrate();
        }
    );

    public static string ConnectionString => Database.ConnectionString;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
