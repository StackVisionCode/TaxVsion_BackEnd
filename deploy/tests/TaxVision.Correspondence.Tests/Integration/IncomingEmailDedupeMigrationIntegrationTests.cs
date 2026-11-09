extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Correspondence.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-07: con duplicados en producción la migración del índice no frena el deploy ni borra correos.</summary>
public sealed class IncomingEmailDedupeMigrationIntegrationTests
{
    [Fact(Timeout = 180_000)]
    public async Task Migration_marks_the_duplicate_and_keeps_both_emails()
    {
        await using var database = new MigrationTestDatabase(ServerConnection(), "cor_migdup");
        await using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(database.ConnectionString).Options,
            new NoTenant()
        );
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(
            migrations[migrations.FindIndex(m => m.EndsWith("_AddIncomingEmailProviderMessageDedupeIndex")) - 1]
        );
        var (tenant, account) = (Guid.NewGuid(), Guid.NewGuid());
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var (id, receivedAt) in new[] { (second, "2026-11-02T10:00:00"), (first, "2026-11-01T10:00:00") })
            await database.InsertAsync(
                "IncomingEmails",
                new Dictionary<string, object?>
                {
                    ["Id"] = id,
                    ["TenantId"] = tenant,
                    ["AccountId"] = account,
                    ["ProviderMessageId"] = "provider-1",
                    ["InternetMessageId"] = null,
                    ["ReceivedAtUtc"] = DateTime.Parse(receivedAt),
                }
            );

        await migrator.MigrateAsync();

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM IncomingEmails"));
        Assert.Equal(
            first,
            await database.ScalarAsync<Guid>("SELECT Id FROM IncomingEmails WHERE InternetMessageId IS NULL")
        );
        Assert.Equal(
            $"<duplicate-{second}@taxvision.invalid>",
            await database.ScalarAsync<string>($"SELECT InternetMessageId FROM IncomingEmails WHERE Id = '{second}'"),
            ignoreCase: true
        );
    }

    private static string ServerConnection() =>
        Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("236deafb-0353-4dac-8aed-b15fbb76ae20")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set CORRESPONDENCE_SQLSERVER_TESTS or the Api user-secrets.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
