using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>PMS-05: el mismo aviso sin dueño no se guarda dos veces.</summary>
[Collection(PostmasterTemporaryDatabase.Collection)]
public sealed class UnmatchedDeliveryFeedbackIntegrationTests : IAsyncLifetime
{
    private readonly PostmasterTemporaryDatabase _database = new("pmsunmatched");

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static UnmatchedDeliveryFeedback Unmatched() =>
        UnmatchedDeliveryFeedback.Create("Smtp2Go", "evt-1", "c***@example.test", "hint", "{}", DateTime.UtcNow);

    // Si en producción hubiera duplicados, la migración se detiene con un mensaje claro y no toca los datos.
    [Fact(Timeout = 120_000)]
    public async Task Migration_stops_with_a_clear_message_when_duplicates_exist()
    {
        await using var database = new PostmasterTemporaryDatabase("pmsmigration");
        await database.DropOrphansAsync();
        await using var db = database.NewContext();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var before = migrations[migrations.FindIndex(m => m.EndsWith("_AddUnmatchedDeliveryFeedbackUniqueIndex")) - 1];
        await migrator.MigrateAsync(before);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO UnmatchedDeliveryFeedback (Id, Source, SourceEventId, ReceivedAtUtc)
            VALUES (NEWID(), 'Smtp2Go', 'evt-1', SYSUTCDATETIME()), (NEWID(), 'Smtp2Go', 'evt-1', SYSUTCDATETIME())
            """
        );

        var failure = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync());

        Assert.Contains("run Q-PMS-4", failure.Message);
        Assert.Equal(2, await db.UnmatchedDeliveryFeedback.CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Same_source_event_twice_is_rejected()
    {
        await using (var first = _database.NewContext())
        {
            first.UnmatchedDeliveryFeedback.Add(Unmatched());
            await first.SaveChangesAsync();
        }

        await using var second = _database.NewContext();
        second.UnmatchedDeliveryFeedback.Add(Unmatched());
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
        await using var read = _database.NewContext();
        Assert.Equal(1, await read.UnmatchedDeliveryFeedback.CountAsync());
    }
}
