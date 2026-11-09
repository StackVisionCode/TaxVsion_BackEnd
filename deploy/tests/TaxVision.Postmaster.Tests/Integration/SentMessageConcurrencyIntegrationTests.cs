using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>PMS-03 contra SQL real: dos avisos leídos a la vez no se pisan el estado del correo.</summary>
[Collection(PostmasterTemporaryDatabase.Collection)]
public sealed class SentMessageConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string First = "first@example.test";
    private const string Second = "second@example.test";
    private static readonly DateTime SentAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly PostmasterTemporaryDatabase _database = new("pmsconc");

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static Task<SentMessage> LoadAsync(PostmasterDbContext db, Guid id) =>
        db.SentMessages.IgnoreQueryFilters().Include(m => m.Recipients).SingleAsync(m => m.Id == id);

    private static DeliveryFeedback Outcome(DeliveryOutcome outcome, DateTime at, string eventId) =>
        DeliveryFeedback.ForOutcome(outcome, at, FeedbackSource.Smtp2Go, eventId, null).Value;

    private static DeliveryFeedback Opened(DateTime at, string eventId) =>
        DeliveryFeedback.ForSignal(EngagementSignal.Opened, at, FeedbackSource.Smtp2Go, eventId).Value;

    private static void Apply(SentMessage message, string address, DeliveryFeedback feedback) =>
        Assert.True(
            message.ApplyDeliveryFeedback(message.FindRecipientByAddress(address)!.Id, feedback, "{}").IsSuccess
        );

    [Fact(Timeout = 120_000)]
    public async Task Deferred_and_delivered_for_same_recipient_second_save_conflicts()
    {
        var seeded = await _database.SeedSentAsync(SentAt, First);

        await using var delivered = _database.NewContext();
        await using var deferred = _database.NewContext();
        var winner = await LoadAsync(delivered, seeded.Id);
        var stale = await LoadAsync(deferred, seeded.Id);

        Apply(winner, First, Outcome(DeliveryOutcome.Delivered, SentAt.AddMinutes(2), "evt-delivered"));
        await delivered.SaveChangesAsync();

        Apply(stale, First, Outcome(DeliveryOutcome.Deferred, SentAt.AddMinutes(1), "evt-deferred"));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => deferred.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _database.NewContext();
        Assert.Equal(
            DeliveryOutcome.Delivered,
            (await LoadAsync(read, seeded.Id)).FindRecipientByAddress(First)!.Outcome
        );
    }

    [Fact(Timeout = 120_000)]
    public async Task Bounce_and_delivery_on_two_recipients_keep_bounce_visible()
    {
        var seeded = await _database.SeedSentAsync(SentAt, First, Second);
        var bounce = Outcome(DeliveryOutcome.HardBounced, SentAt.AddMinutes(3), "evt-bounce");
        var delivery = Outcome(DeliveryOutcome.Delivered, SentAt.AddMinutes(2), "evt-delivery");
        await using (var seed = _database.NewContext())
        {
            // Así el perdedor sí recalcula Activity, de Deferred a Sent, y lo escribiría encima del rebote.
            Apply(
                await LoadAsync(seed, seeded.Id),
                Second,
                Outcome(DeliveryOutcome.Deferred, SentAt.AddMinutes(1), "evt-seed")
            );
            await seed.SaveChangesAsync();
        }

        await using var bounced = _database.NewContext();
        await using var delivered = _database.NewContext();
        var winner = await LoadAsync(bounced, seeded.Id);
        var stale = await LoadAsync(delivered, seeded.Id);

        Apply(winner, First, bounce);
        await bounced.SaveChangesAsync();

        Apply(stale, Second, delivery);
        try
        {
            await delivered.SaveChangesAsync();
        }
        catch (ConflictException ex) when (ex.Code == "Persistence.ConcurrencyConflict")
        {
            // Lo que haría Wolverine: un intento nuevo relee y vuelve a aplicar.
            await using var retry = _database.NewContext();
            Apply(await LoadAsync(retry, seeded.Id), Second, delivery);
            await retry.SaveChangesAsync();
        }

        await using var read = _database.NewContext();
        var final = await LoadAsync(read, seeded.Id);
        Assert.Equal(SentMessageActivity.Bounced, final.Activity);
        Assert.Equal(DeliveryOutcome.Delivered, final.FindRecipientByAddress(Second)!.Outcome);
        Assert.Equal(DeliveryOutcome.HardBounced, final.FindRecipientByAddress(First)!.Outcome);
    }

    // Un aviso viejo no mueve LastEventAtUtc ni Activity: sin tocar la raíz, el contador se pierde sin conflicto.
    [Fact(Timeout = 120_000)]
    public async Task Recipient_change_touches_the_root()
    {
        var seeded = await _database.SeedSentAsync(SentAt, First);
        await using (var seed = _database.NewContext())
        {
            Apply(await LoadAsync(seed, seeded.Id), First, Opened(SentAt.AddHours(1), "evt-seed"));
            await seed.SaveChangesAsync();
        }

        await using var newer = _database.NewContext();
        await using var older = _database.NewContext();
        var winner = await LoadAsync(newer, seeded.Id);
        var stale = await LoadAsync(older, seeded.Id);

        Apply(winner, First, Opened(SentAt.AddHours(2), "evt-newer"));
        await newer.SaveChangesAsync();

        Apply(stale, First, Opened(SentAt.AddMinutes(10), "evt-older"));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => older.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _database.NewContext();
        Assert.Equal(2, (await LoadAsync(read, seeded.Id)).FindRecipientByAddress(First)!.OpenCount);
    }
}
