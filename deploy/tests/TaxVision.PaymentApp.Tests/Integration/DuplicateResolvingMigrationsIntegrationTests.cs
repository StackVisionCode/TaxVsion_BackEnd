extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>PAY-4 y PAY-6: con duplicados en producción las migraciones de índice no frenan el deploy ni borran pagos.</summary>
public sealed class DuplicateResolvingMigrationsIntegrationTests
{
    [Fact(Timeout = 180_000)]
    public async Task External_references_migration_keeps_them_on_the_oldest_payment()
    {
        await using var database = new MigrationTestDatabase(ServerConnection(), "pay_migdup");
        var (db, migrator) = await MigrateUntilAsync(database, "_AddSaaSPaymentExternalReferenceIndexes");
        await using var _ = db;
        var (oldCharge, newCharge, oldSession, newSession) = (
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        );
        await InsertPaymentAsync(database, oldCharge, "2026-11-01", charge: "ch_1", session: null);
        await InsertPaymentAsync(database, newCharge, "2026-11-02", charge: "ch_1", session: null);
        await InsertPaymentAsync(database, oldSession, "2026-11-01", charge: null, session: "cs_1");
        await InsertPaymentAsync(database, newSession, "2026-11-02", charge: null, session: "cs_1");

        await migrator.MigrateAsync();

        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM SaaSPayments"));
        Assert.Equal(
            oldCharge,
            await database.ScalarAsync<Guid>("SELECT Id FROM SaaSPayments WHERE ExternalChargeReference = 'ch_1'")
        );
        Assert.Equal(
            oldSession,
            await database.ScalarAsync<Guid>("SELECT Id FROM SaaSPayments WHERE ProviderCheckoutSessionId = 'cs_1'")
        );
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM SaaSPayments WHERE Id = '{newCharge}' AND (ExternalChargeProvider IS NOT NULL OR ExternalChargeReference IS NOT NULL)"
            )
        );
    }

    [Fact(Timeout = 180_000)]
    public async Task One_default_migration_keeps_the_newest_default()
    {
        await using var database = new MigrationTestDatabase(ServerConnection(), "pay_migdef");
        var (db, migrator) = await MigrateUntilAsync(database, "_AddTenantProviderCustomerConcurrency");
        await using var _ = db;
        var customer = Guid.NewGuid();
        await database.InsertAsync("TenantProviderCustomers", new Dictionary<string, object?> { ["Id"] = customer });
        var (older, newer, detached) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (
            var (id, createdAt, isDetached) in new[]
            {
                (older, "2026-11-01", false),
                (newer, "2026-11-02", false),
                (detached, "2026-11-03", true),
            }
        )
            await database.InsertAsync(
                "TenantSavedPaymentMethods",
                new Dictionary<string, object?>
                {
                    ["Id"] = id,
                    ["TenantProviderCustomerId"] = customer,
                    ["IsDefault"] = true,
                    ["IsDetached"] = isDetached,
                    ["CreatedAtUtc"] = DateTime.Parse(createdAt),
                }
            );

        await migrator.MigrateAsync();

        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM TenantSavedPaymentMethods"));
        Assert.Equal(
            newer,
            await database.ScalarAsync<Guid>(
                "SELECT Id FROM TenantSavedPaymentMethods WHERE IsDefault = 1 AND IsDetached = 0"
            )
        );
    }

    private static Task InsertPaymentAsync(
        MigrationTestDatabase database,
        Guid id,
        string createdAt,
        string? charge,
        string? session
    ) =>
        database.InsertAsync(
            "SaaSPayments",
            new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["CreatedAtUtc"] = DateTime.Parse(createdAt),
                ["ExternalChargeProvider"] = charge is null ? null : "Stripe",
                ["ExternalChargeReference"] = charge,
                ["ProviderCheckoutSessionId"] = session,
            }
        );

    private static async Task<(PaymentAppDbContext Db, IMigrator Migrator)> MigrateUntilAsync(
        MigrationTestDatabase database,
        string migrationSuffix
    )
    {
        var db = new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(database.ConnectionString).Options,
            new NoTenant()
        );
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[migrations.FindIndex(m => m.EndsWith(migrationSuffix)) - 1]);
        return (db, migrator);
    }

    private static string ServerConnection() =>
        Environment.GetEnvironmentVariable("PAYMENTAPP_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-paymentapp001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set PAYMENTAPP_DB_CONNECTION or the PaymentApp user-secrets.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
