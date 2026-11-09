using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class ReconcileDeliveryFeedbackHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>Anota a quién le preguntaron y contesta "entregado" para cada uno.</summary>
    private sealed class FakePoller : IDeliveryFeedbackPoller
    {
        public List<PendingDeliveryRef> Asked { get; } = [];

        public FeedbackSource Source => FeedbackSource.Smtp2Go;

        public Task<IReadOnlyList<TranslatedFeedback>> PollAsync(
            IReadOnlyList<PendingDeliveryRef> pending,
            CancellationToken ct
        )
        {
            Asked.AddRange(pending);
            IReadOnlyList<TranslatedFeedback> answers = pending
                .Select(p => new TranslatedFeedback(
                    null,
                    null,
                    p.ProviderMessageId,
                    p.RecipientAddress,
                    DeliveryFeedback
                        .ForOutcome(
                            DeliveryOutcome.Delivered,
                            Now,
                            FeedbackSource.Smtp2Go,
                            $"poll-{p.RecipientId:N}",
                            null
                        )
                        .Value,
                    "{}"
                ))
                .ToList();
            return Task.FromResult(answers);
        }
    }

    private static PostmasterDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private static async Task<SentMessage> AddSentAsync(
        PostmasterDbContext db,
        DateTime sentAt,
        DeliveryLane lane = DeliveryLane.SystemProvider,
        string? providerReference = "email-id"
    )
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@taxoffix.com",
                EmailStream.Transactional,
                "system-smtp",
                null,
                null,
                null,
                null,
                null,
                sentAt,
                deliveryLane: lane
            )
            .Value;
        message.AddRecipient("customer@example.com", RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent(providerReference, sentAt);
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return message;
    }

    /// <summary>El comando de aplicar corre en línea, como <c>InvokeAsync</c>, sobre el mismo contexto.</summary>
    private static FakeMessageBus ApplyingBus(PostmasterDbContext db)
    {
        var bus = new FakeMessageBus();
        bus.OnInvoke = async message =>
            await ApplyPolledDeliveryFeedbackHandler.Handle(
                (ApplyPolledDeliveryFeedbackCommand)message,
                new SentMessageRepository(db),
                new DeliveryFeedbackReceiptRepository(db),
                new UnmatchedDeliveryFeedbackRepository(db),
                new SuppressionListRepository(db),
                db,
                bus,
                NullLogger.Instance,
                CancellationToken.None
            );
        return bus;
    }

    private static Task<DeliveryFeedbackTally> ReconcileAsync(
        PostmasterDbContext db,
        IDeliveryFeedbackPoller? poller,
        int max = 50,
        FakeMessageBus? bus = null
    ) =>
        ReconcileDeliveryFeedbackHandler.Handle(
            new ReconcileDeliveryFeedbackCommand(Now.AddHours(-72), Now.AddHours(-6), max, Now, ApplyBatchSize: 50),
            new DeliveryFeedbackCapabilities([], poller is null ? [] : [poller], []),
            new SentMessageRepository(db),
            bus ?? ApplyingBus(db),
            NullLogger.Instance,
            CancellationToken.None
        );

    // Un choque con el webhook deja ese lote para la próxima vuelta y no corta los demás.
    [Fact]
    public async Task Conflict_in_one_batch_keeps_the_others()
    {
        await using var db = CreateContext();
        await AddSentAsync(db, Now.AddHours(-10));
        await AddSentAsync(db, Now.AddHours(-10));
        var bus = ApplyingBus(db);
        var calls = 0;
        bus.BeforeInvoke = _ =>
        {
            if (++calls == 1)
                throw new BuildingBlocks.Results.ConflictException("Persistence.ConcurrencyConflict", "Modified.");
        };

        var tally = await ReconcileDeliveryFeedbackHandler.Handle(
            new ReconcileDeliveryFeedbackCommand(Now.AddHours(-72), Now.AddHours(-6), 50, Now, ApplyBatchSize: 1),
            new DeliveryFeedbackCapabilities([], [new FakePoller()], []),
            new SentMessageRepository(db),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(2, calls);
        Assert.Equal(1, tally.Applied);
    }

    [Fact]
    public async Task Handle_asks_only_for_system_emails_inside_the_window_without_final_outcome()
    {
        await using var db = CreateContext();
        var pending = await AddSentAsync(db, Now.AddHours(-10));
        await AddSentAsync(db, Now.AddHours(-1)); // muy reciente: el webhook todavía puede llegar
        await AddSentAsync(db, Now.AddHours(-100)); // muy viejo: ya no cambia
        await AddSentAsync(db, Now.AddHours(-10), DeliveryLane.TenantMailbox); // no salió por SMTP2GO
        await AddSentAsync(db, Now.AddHours(-10), providerReference: null); // sin id con qué buscar
        var delivered = await AddSentAsync(db, Now.AddHours(-10));
        delivered.ApplyDeliveryFeedback(
            delivered.Recipients.Single().Id,
            DeliveryFeedback.ForOutcome(DeliveryOutcome.Delivered, Now, FeedbackSource.Smtp2Go, "evt-d", null).Value,
            null
        );
        await db.SaveChangesAsync();
        var poller = new FakePoller();

        var tally = await ReconcileAsync(db, poller);

        var asked = Assert.Single(poller.Asked);
        Assert.Equal(pending.Id, asked.SentMessageId);
        Assert.Equal(1, tally.Applied);
        Assert.Equal(
            DeliveryOutcome.Delivered,
            db.SentMessageRecipients.IgnoreQueryFilters().Single(r => r.SentMessageId == pending.Id).Outcome
        );
    }

    [Fact]
    public async Task Handle_respects_the_cap_per_run()
    {
        await using var db = CreateContext();
        for (var i = 0; i < 3; i++)
            await AddSentAsync(db, Now.AddHours(-10 - i));
        var poller = new FakePoller();

        await ReconcileAsync(db, poller, max: 2);

        // Los más viejos primero; el resto espera a la próxima corrida.
        Assert.Equal(2, poller.Asked.Count);
        Assert.True(poller.Asked[0].SentAtUtc < poller.Asked[1].SentAtUtc);
    }

    [Fact]
    public async Task Handle_does_nothing_without_a_poller()
    {
        await using var db = CreateContext();
        await AddSentAsync(db, Now.AddHours(-10));

        var tally = await ReconcileAsync(db, poller: null);

        Assert.Equal(DeliveryFeedbackTally.Empty, tally);
    }
}
