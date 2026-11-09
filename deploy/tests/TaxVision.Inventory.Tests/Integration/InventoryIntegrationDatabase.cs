extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Inventory.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Inventory.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Inventory (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class InventoryIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "inv_tint",
        () =>
            Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e3b1a9f6-2c8d-4a17-9b54-7f2e6c1d8a90")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set INVENTORY_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new InventoryDbContext(
                new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(connection).Options,
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
