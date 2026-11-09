using BuildingBlocks.Results;
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

    // Con el mismo aviso repetido la migración deja el primero recibido y crea el índice (decisión del dueño, 2026-11-09).
    [Fact(Timeout = 120_000)]
    public async Task Migration_keeps_the_first_of_each_duplicate()
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
            INSERT INTO UnmatchedDeliveryFeedback (Id, Source, SourceEventId, ReceivedAtUtc, CorrelationHint)
            VALUES (NEWID(), 'Smtp2Go', 'evt-1', '2026-11-01T10:00:00', 'first'),
                   (NEWID(), 'Smtp2Go', 'evt-1', '2026-11-01T11:00:00', 'second'),
                   (NEWID(), 'Smtp2Go', 'evt-1', '2026-11-01T12:00:00', 'third'),
                   (NEWID(), 'Smtp2Go', 'evt-2', '2026-11-01T10:00:00', 'other')
            """
        );

        await migrator.MigrateAsync();

        var kept = await db.UnmatchedDeliveryFeedback.AsNoTracking().OrderBy(f => f.SourceEventId).ToListAsync();
        Assert.Equal(["first", "other"], kept.Select(f => f.CorrelationHint));
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
