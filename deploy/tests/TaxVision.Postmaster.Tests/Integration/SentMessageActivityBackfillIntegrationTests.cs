using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Seed;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>PMS-03: la reposición corrige lo que Q-PMS-2 devuelve, sin tocar los resultados de los destinatarios.</summary>
[Collection(PostmasterTemporaryDatabase.Collection)]
public sealed class SentMessageActivityBackfillIntegrationTests : IAsyncLifetime
{
    private const string Recipient = "customer@example.test";
    private static readonly DateTime SentAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly PostmasterTemporaryDatabase _database = new("pmsactivity");

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static Task<SentMessage> LoadAsync(PostmasterDbContext db, Guid id) =>
        db.SentMessages.IgnoreQueryFilters().Include(m => m.Recipients).SingleAsync(m => m.Id == id);

    // La carrera de antes de PMS-03 dejaba el rebote en el destinatario y Activity sin él.
    private async Task<Guid> SeedHiddenBounceAsync()
    {
        var message = await _database.SeedSentAsync(SentAt, Recipient);
        await using var db = _database.NewContext();
        var loaded = await LoadAsync(db, message.Id);
        loaded.ApplyDeliveryFeedback(
            loaded.FindRecipientByAddress(Recipient)!.Id,
            DeliveryFeedback
                .ForOutcome(DeliveryOutcome.HardBounced, SentAt.AddMinutes(1), FeedbackSource.Smtp2Go, "evt-1", null)
                .Value,
            "{}"
        );
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SentMessages SET Activity = 'Delivered' WHERE Id = {message.Id}"
        );
        return message.Id;
    }

    [Fact(Timeout = 120_000)]
    public async Task Restores_the_hidden_bounce_once_and_leaves_outcomes_alone()
    {
        var id = await SeedHiddenBounceAsync();
        await _database.SeedSentAsync(SentAt, "healthy@example.test");

        await using var run = _database.NewContext();
        var first = await SentMessageActivityBackfill.RunAsync(run, batchSize: 1, dryRun: false, default);
        var second = await SentMessageActivityBackfill.RunAsync(run, batchSize: 1, dryRun: false, default);

        Assert.Equal(1, first.Found);
        Assert.Equal(1, first.Changed);
        Assert.Equal(0, second.Found);
        await using var read = _database.NewContext();
        var message = await LoadAsync(read, id);
        Assert.Equal(SentMessageActivity.Bounced, message.Activity);
        Assert.Equal(DeliveryOutcome.HardBounced, message.FindRecipientByAddress(Recipient)!.Outcome);
    }

    [Fact(Timeout = 120_000)]
    public async Task Dry_run_counts_without_writing()
    {
        var id = await SeedHiddenBounceAsync();

        await using var run = _database.NewContext();
        var outcome = await SentMessageActivityBackfill.RunAsync(run, batchSize: 50, dryRun: true, default);

        Assert.Equal(1, outcome.Changed);
        await using var read = _database.NewContext();
        Assert.Equal(SentMessageActivity.Delivered, (await LoadAsync(read, id)).Activity);
    }
}
