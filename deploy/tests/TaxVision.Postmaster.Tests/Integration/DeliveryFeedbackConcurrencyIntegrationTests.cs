using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// PMS-05 contra SQL real: el choque de la supresión no se confunde con un aviso repetido. Antes, el segundo rebote
/// a la misma dirección se tomaba por duplicado y su destinatario se quedaba sin rebote.
/// </summary>
[Collection(PostmasterTemporaryDatabase.Collection)]
public sealed class DeliveryFeedbackConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string Recipient = "bounced@example.test";
    private static readonly DateTime SentAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly PostmasterTemporaryDatabase _database = new("pmsfeed");

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private sealed class FixedTranslator(TranslatedFeedback item) : IDeliveryFeedbackTranslator
    {
        public FeedbackSource Source => FeedbackSource.Smtp2Go;

        public Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload) =>
            Result.Success<IReadOnlyList<TranslatedFeedback>>([item]);
    }

    /// <summary>Retiene a cada lector hasta que los dos vieron "no está suprimida".</summary>
    private sealed class RacingSuppressionList(
        ISuppressionListRepository inner,
        SemaphoreSlim arrived,
        SemaphoreSlim go
    ) : ISuppressionListRepository
    {
        public async Task<Result<SuppressionListEntry>> GetByAddressAsync(
            Guid tenantId,
            string address,
            CancellationToken ct = default
        )
        {
            var result = await inner.GetByAddressAsync(tenantId, address, ct);
            arrived.Release();
            await go.WaitAsync(ct);
            return result;
        }

        public Task AddAsync(SuppressionListEntry entry, CancellationToken ct = default) => inner.AddAsync(entry, ct);

        public Task<IReadOnlyList<SuppressionListEntry>> ListAsync(
            Guid tenantId,
            string? addressFilter,
            SuppressionReason? reasonFilter,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => inner.ListAsync(tenantId, addressFilter, reasonFilter, page, pageSize, ct);

        public Task<IReadOnlySet<string>> GetSuppressedAsync(
            Guid tenantId,
            IReadOnlyCollection<string> normalizedAddresses,
            CancellationToken ct = default
        ) => inner.GetSuppressedAsync(tenantId, normalizedAddresses, ct);

        public Task<bool> RemoveAsync(Guid tenantId, string emailAddress, CancellationToken ct = default) =>
            inner.RemoveAsync(tenantId, emailAddress, ct);
    }

    private static TranslatedFeedback HardBounce(SentMessage message, string eventId) =>
        new(
            null,
            null,
            $"provider-{message.Id:N}",
            Recipient,
            DeliveryFeedback
                .ForOutcome(DeliveryOutcome.HardBounced, SentAt.AddMinutes(5), FeedbackSource.Smtp2Go, eventId, "550")
                .Value,
            "{}"
        );

    private static Task<Result<DeliveryFeedbackTally>> ProcessAsync(
        PostmasterDbContext db,
        TranslatedFeedback item,
        ISuppressionListRepository suppressionList
    ) =>
        ProcessDeliveryFeedbackHandler.Handle(
            new ProcessDeliveryFeedbackCommand("Smtp2Go", "{}", SentAt.AddMinutes(10)),
            new DeliveryFeedbackCapabilities([new FixedTranslator(item)], [], []),
            new SentMessageRepository(db),
            new DeliveryFeedbackReceiptRepository(db),
            new UnmatchedDeliveryFeedbackRepository(db),
            suppressionList,
            db,
            new FakeMessageBus(),
            NullLogger.Instance,
            CancellationToken.None
        );

    [Fact(Timeout = 120_000)]
    public async Task Two_hard_bounces_to_same_address_both_apply()
    {
        var tenantId = Guid.NewGuid();
        var first = await SeedForTenantAsync(tenantId);
        var second = await SeedForTenantAsync(tenantId);
        var arrived = new SemaphoreSlim(0);
        var go = new SemaphoreSlim(0);

        await using var firstDb = _database.NewContext();
        await using var secondDb = _database.NewContext();
        var firstItem = HardBounce(first, "evt-first");
        var secondItem = HardBounce(second, "evt-second");
        var racing = new[]
        {
            AttemptAsync(firstDb, firstItem, arrived, go),
            AttemptAsync(secondDb, secondItem, arrived, go),
        };
        await arrived.WaitAsync();
        await arrived.WaitAsync();
        go.Release(2);
        var outcomes = await Task.WhenAll(racing);

        // Lo que haría Wolverine con el perdedor: un intento nuevo, que ya ve la supresión.
        foreach (var (failed, item) in outcomes.Zip([firstItem, secondItem]))
            if (failed)
            {
                await using var retry = _database.NewContext();
                var result = await ProcessAsync(retry, item, new SuppressionListRepository(retry));
                Assert.Equal(1, result.Value.Applied);
            }

        Assert.Single(outcomes, failed => failed);
        await using var read = _database.NewContext();
        Assert.Equal(
            2,
            await read
                .SentMessageRecipients.IgnoreQueryFilters()
                .CountAsync(r => r.Address == Recipient && r.Outcome == DeliveryOutcome.HardBounced)
        );
        Assert.Equal(1, await read.SuppressionListEntries.CountAsync());
        Assert.Equal(2, await read.DeliveryFeedbackReceipts.CountAsync());
    }

    // Una copia repetida del mismo aviso sigue siendo un duplicado, no un error.
    [Fact(Timeout = 120_000)]
    public async Task Same_event_twice_at_once_is_still_a_duplicate()
    {
        var message = await SeedForTenantAsync(Guid.NewGuid());
        var item = HardBounce(message, "evt-copy");
        await using var firstDb = _database.NewContext();
        await using var secondDb = _database.NewContext();

        // Las dos copias pasaron ExistsAsync: al confirmar, una gana y la otra choca con el recibo.
        var receipts = new DeliveryFeedbackReceiptRepository(firstDb);
        var loser = new DeliveryFeedbackReceiptRepository(secondDb);
        var receipt = () => DeliveryFeedbackReceipt.Create("Smtp2Go", "evt-copy", message.Id, SentAt);

        Assert.True(await receipts.TryCommitAsync(receipt()));
        Assert.False(await loser.TryCommitAsync(receipt()));
    }

    private async Task<bool> AttemptAsync(
        PostmasterDbContext db,
        TranslatedFeedback item,
        SemaphoreSlim arrived,
        SemaphoreSlim go
    )
    {
        try
        {
            await ProcessAsync(db, item, new RacingSuppressionList(new SuppressionListRepository(db), arrived, go));
            return false;
        }
        catch (ConflictException)
        {
            return true;
        }
    }

    private async Task<SentMessage> SeedForTenantAsync(Guid tenantId)
    {
        var message = SentMessage
            .Queue(
                tenantId,
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@example.test",
                EmailStream.Transactional,
                "system-smtp",
                null,
                null,
                null,
                null,
                null,
                SentAt
            )
            .Value;
        message.AddRecipient(Recipient, RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent($"provider-{message.Id:N}", SentAt);
        await using var db = _database.NewContext();
        db.SentMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }
}
