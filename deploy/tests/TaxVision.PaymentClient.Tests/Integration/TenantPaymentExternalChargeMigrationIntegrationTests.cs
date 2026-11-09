extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>PCL-6: con duplicados en producción la migración del índice no frena el deploy ni borra pagos.</summary>
public sealed class TenantPaymentExternalChargeMigrationIntegrationTests
{
    [Fact(Timeout = 180_000)]
    public async Task Migration_keeps_the_reference_on_the_oldest_payment_of_each_tenant()
    {
        await using var database = new MigrationTestDatabase(ServerConnection(), "payc_migdup");
        await using var db = new PaymentClientDbContext(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(database.ConnectionString).Options,
            new NoTenant()
        );
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(
            migrations[migrations.FindIndex(m => m.EndsWith("_AddTenantPaymentExternalChargeIndex")) - 1]
        );
        var (tenant, otherTenant) = (Guid.NewGuid(), Guid.NewGuid());
        var (older, newer, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (
            var (id, tenantId, createdAt) in new[]
            {
                (newer, tenant, "2026-11-02"),
                (older, tenant, "2026-11-01"),
                (other, otherTenant, "2026-11-03"),
            }
        )
            await database.InsertAsync(
                "TenantPayments",
                new Dictionary<string, object?>
                {
                    ["Id"] = id,
                    ["TenantId"] = tenantId,
                    ["CreatedAtUtc"] = DateTime.Parse(createdAt),
                    ["ExternalChargeProvider"] = "Stripe",
                    ["ExternalChargeReference"] = "pi_1",
                }
            );

        await migrator.MigrateAsync();

        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM TenantPayments"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM TenantPayments WHERE ExternalChargeReference = 'pi_1'"
            )
        );
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM TenantPayments WHERE Id = '{newer}' AND (ExternalChargeProvider IS NOT NULL OR ExternalChargeReference IS NOT NULL)"
            )
        );
    }

    private static string ServerConnection() =>
        Environment.GetEnvironmentVariable("PAYMENTCLIENT_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("7c2a4f19-3e6b-4d02-8a91-paymentclient1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set PAYMENTCLIENT_DB_CONNECTION or the Api user-secrets.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
