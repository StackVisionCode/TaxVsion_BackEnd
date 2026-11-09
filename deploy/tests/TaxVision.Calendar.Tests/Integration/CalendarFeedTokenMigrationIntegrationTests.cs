extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>CAL-4: con dos tokens activos en producción la migración no frena el deploy ni borra tokens.</summary>
public sealed class CalendarFeedTokenMigrationIntegrationTests
{
    [Fact(Timeout = 180_000)]
    public async Task Migration_keeps_the_newest_token_active()
    {
        await using var database = new MigrationTestDatabase(ServerConnection(), "cal_migdup");
        await using var db = new CalendarDbContext(
            new DbContextOptionsBuilder<CalendarDbContext>().UseSqlServer(database.ConnectionString).Options,
            new NoTenant()
        );
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(
            migrations[migrations.FindIndex(m => m.EndsWith("_AddCalendarFeedTokenActivePerUserIndex")) - 1]
        );
        var (tenant, user) = (Guid.NewGuid(), Guid.NewGuid());
        var (older, newer) = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var (id, createdAt) in new[] { (newer, "2026-11-02"), (older, "2026-11-01") })
            await database.InsertAsync(
                "CalendarFeedTokens",
                new Dictionary<string, object?>
                {
                    ["Id"] = id,
                    ["TenantId"] = tenant,
                    ["UserId"] = user,
                    ["CreatedAtUtc"] = DateTime.Parse(createdAt),
                    ["RevokedAtUtc"] = null,
                }
            );

        await migrator.MigrateAsync();

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM CalendarFeedTokens"));
        Assert.Equal(
            newer,
            await database.ScalarAsync<Guid>("SELECT Id FROM CalendarFeedTokens WHERE RevokedAtUtc IS NULL")
        );
    }

    private static string ServerConnection() =>
        Environment.GetEnvironmentVariable("CALENDAR_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("b7f4a1d2-9c3e-4a58-8f21-6d0e5b3c74a9")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set CALENDAR_DB_CONNECTION or the Calendar user-secrets.");

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
