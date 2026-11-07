extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Base temporal de las T-INT de Subscription (BB-10), una por corrida: la flota no la lee, así que una corrida abortada no le
/// deja filas ni sobres. La base local solo ubica el servidor.
/// </summary>
internal static class SubscriptionIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "sub_tint",
        () =>
            Environment.GetEnvironmentVariable("SUBSCRIPTION_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-subscription1")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set SUBSCRIPTION_DB_CONNECTION or the Api user-secrets to run this test."
            ),
        connection =>
        {
            using var db = new SubscriptionDbContext(
                new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(connection).Options,
                new NoTenant()
            );
            db.Database.Migrate();
            SeedCatalog(db).GetAwaiter().GetResult();
        }
    );

    public static string ConnectionString => Database.ConnectionString;

    // Los mismos seeders de Program.cs: los planes no vienen por migración y las pruebas compran sobre ellos.
    private static async Task SeedCatalog(SubscriptionDbContext db)
    {
        await SubscriptionModuleCatalogSeeder.SeedAsync(
            db,
            new SubscriptionCatalogOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );
        await SubscriptionPlanCatalogSeeder.SeedAsync(db, CancellationToken.None);
        await SubscriptionAddOnCatalogSeeder.SeedAsync(db, CancellationToken.None);
        await SubscriptionSeatPricingSeeder.SeedAsync(db, CancellationToken.None);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
