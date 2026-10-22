using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class ProcessDeliveryFeedbackHandlerTests
{
    private const string Recipient = "customer@example.com";
    private static readonly DateTime SentAt = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Los avisos llegan sin oficina: el handler no debe depender del filtro de tenant.</summary>
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>Devuelve lo que el test le cargue: el handler no sabe nada del proveedor.</summary>
    private sealed class FakeTranslator(IReadOnlyList<TranslatedFeedback> items) : IDeliveryFeedbackTranslator
    {
        public FeedbackSource Source => FeedbackSource.Smtp2Go;

        public Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload) => Result.Success(items);
    }

    private sealed class Fixture(PostmasterDbContext db, SentMessage message)
    {
        public PostmasterDbContext Db { get; } = db;
        public SentMessage Message { get; } = message;
        public FakeMessageBus Bus { get; } = new();

        public Task<Result<DeliveryFeedbackTally>> ProcessAsync(params TranslatedFeedback[] items) =>
            ProcessDeliveryFeedbackHandler.Handle(
                new ProcessDeliveryFeedbackCommand("Smtp2Go", "{}", SentAt.AddMinutes(10)),
                new DeliveryFeedbackCapabilities([new FakeTranslator(items)], []),
                new SentMessageRepository(Db),
                new DeliveryFeedbackReceiptRepository(Db),
                new UnmatchedDeliveryFeedbackRepository(Db),
                new SuppressionListRepository(Db),
                Db,
                Bus,
                NullLogger.Instance,
                CancellationToken.None
            );

        public SentMessageRecipient LoadRecipient() =>
            Db.SentMessageRecipients.IgnoreQueryFilters().Single(r => r.SentMessageId == Message.Id);
    }

    private static readonly Guid NotificationLogId = Guid.NewGuid();
    private static readonly Guid DispatchAttemptId = Guid.NewGuid();

    /// <param name="fromNotification">Con log e intento de Notification: a ese correo se le avisan los cambios.</param>
    private static async Task<Fixture> SentMessageInDatabaseAsync(
        string? providerReference = null,
        bool fromNotification = false
    )
    {
        var db = new PostmasterDbContext(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@taxoffix.com",
                EmailStream.Transactional,
                "system-smtp",
                fromNotification ? NotificationLogId : null,
                null,
                null,
                null,
                null,
                SentAt,
                dispatchAttemptId: fromNotification ? DispatchAttemptId : null
            )
            .Value;
        message.AddRecipient(Recipient, RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent(providerReference ?? "message-id", SentAt);
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return new Fixture(db, message);
    }

    private static TranslatedFeedback Delivered(
        string? messageId = null,
        string? correlationHeader = null,
        string? providerMessageId = null,
        string recipient = Recipient,
        string eventId = "evt-1"
    ) =>
        new(
            messageId,
            correlationHeader,
            providerMessageId,
            recipient,
            DeliveryFeedback
                .ForOutcome(DeliveryOutcome.Delivered, SentAt.AddMinutes(1), FeedbackSource.Smtp2Go, eventId, null)
                .Value,
            """{"event":"delivered"}"""
        );

    [Fact]
    public async Task Handle_matches_by_our_message_id()
    {
        var fixture = await SentMessageInDatabaseAsync();
        var ourId = OurMessageId.For(fixture.Message.Id, "no-reply@taxoffix.com");

        var result = await fixture.ProcessAsync(Delivered(messageId: $"<{ourId}>"));

        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(DeliveryOutcome.Delivered, fixture.LoadRecipient().Outcome);
    }

    [Fact]
    public async Task Handle_matches_by_correlation_header_when_the_message_id_was_rewritten()
    {
        var fixture = await SentMessageInDatabaseAsync();

        var result = await fixture.ProcessAsync(
            Delivered(messageId: "<rewritten@relay.net>", correlationHeader: fixture.Message.Id.ToString("N"))
        );

        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(DeliveryOutcome.Delivered, fixture.LoadRecipient().Outcome);
    }

    [Fact]
    public async Task Handle_matches_by_provider_id_and_recipient_as_the_last_resort()
    {
        var fixture = await SentMessageInDatabaseAsync(providerReference: "relay-42");

        var result = await fixture.ProcessAsync(
            Delivered(providerMessageId: "relay-42", recipient: "Customer@Example.com")
        );

        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(DeliveryOutcome.Delivered, fixture.LoadRecipient().Outcome);
    }

    [Fact]
    public async Task Handle_applies_the_same_event_only_once()
    {
        var fixture = await SentMessageInDatabaseAsync();
        var item = Delivered(correlationHeader: fixture.Message.Id.ToString("N"));

        var first = await fixture.ProcessAsync(item);
        var second = await fixture.ProcessAsync(item);

        Assert.Equal(1, first.Value.Applied);
        Assert.Equal(1, second.Value.Duplicates);
        Assert.Equal(0, second.Value.Applied);
        Assert.Single(fixture.Db.DeliveryFeedbackReceipts);
        Assert.Single(
            fixture.Db.SentMessageEvents.IgnoreQueryFilters(),
            e => e.SentMessageId == fixture.Message.Id && e.EventType == SentMessageEventType.Delivered
        );
    }

    [Fact]
    public async Task Handle_stores_unmatched_feedback_with_a_masked_address()
    {
        var fixture = await SentMessageInDatabaseAsync();

        var result = await fixture.ProcessAsync(
            Delivered(messageId: "<someone-else@other.com>", recipient: "stranger@example.com")
        );

        Assert.Equal(1, result.Value.Unmatched);
        var unmatched = Assert.Single(fixture.Db.UnmatchedDeliveryFeedback);
        Assert.Equal("s***@example.com", unmatched.MaskedRecipient);
        Assert.Equal("<someone-else@other.com>", unmatched.CorrelationHint);
        Assert.Empty(fixture.Db.DeliveryFeedbackReceipts);
    }

    [Fact]
    public async Task Handle_treats_a_known_message_with_an_unknown_recipient_as_unmatched()
    {
        var fixture = await SentMessageInDatabaseAsync();

        var result = await fixture.ProcessAsync(
            Delivered(correlationHeader: fixture.Message.Id.ToString("N"), recipient: "other@example.com")
        );

        Assert.Equal(1, result.Value.Unmatched);
    }

    [Fact]
    public async Task Handle_counts_feedback_the_domain_rejects_as_invalid_without_a_receipt()
    {
        var fixture = await SentMessageInDatabaseAsync();
        var accepted = new TranslatedFeedback(
            null,
            fixture.Message.Id.ToString("N"),
            null,
            Recipient,
            DeliveryFeedback.ForOutcome(DeliveryOutcome.Accepted, SentAt, FeedbackSource.Smtp2Go, "evt-x", null).Value,
            "{}"
        );

        var result = await fixture.ProcessAsync(accepted);

        Assert.Equal(1, result.Value.Invalid);
        Assert.Empty(fixture.Db.DeliveryFeedbackReceipts);
    }

    [Fact]
    public async Task Handle_fails_for_a_source_without_translator()
    {
        var fixture = await SentMessageInDatabaseAsync();

        var result = await ProcessDeliveryFeedbackHandler.Handle(
            new ProcessDeliveryFeedbackCommand("Mailgun", "{}", SentAt),
            new DeliveryFeedbackCapabilities([], []),
            new SentMessageRepository(fixture.Db),
            new DeliveryFeedbackReceiptRepository(fixture.Db),
            new UnmatchedDeliveryFeedbackRepository(fixture.Db),
            new SuppressionListRepository(fixture.Db),
            fixture.Db,
            new FakeMessageBus(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal("DeliveryFeedback.UnknownSource", result.Error.Code);
    }

    private static TranslatedFeedback OutcomeFor(Fixture fixture, DeliveryOutcome outcome, string eventId) =>
        new(
            null,
            fixture.Message.Id.ToString("N"),
            null,
            Recipient,
            DeliveryFeedback.ForOutcome(outcome, SentAt.AddMinutes(1), FeedbackSource.Smtp2Go, eventId, "550").Value,
            "{}"
        );

    [Theory]
    [InlineData(DeliveryOutcome.HardBounced, SuppressionReason.HardBounce)]
    [InlineData(DeliveryOutcome.Complained, SuppressionReason.Complaint)]
    public async Task Handle_suppresses_the_address_on_hard_bounce_or_complaint(
        DeliveryOutcome outcome,
        SuppressionReason expectedReason
    )
    {
        var fixture = await SentMessageInDatabaseAsync();

        await fixture.ProcessAsync(OutcomeFor(fixture, outcome, "evt-s"));

        var entry = Assert.Single(fixture.Db.SuppressionListEntries);
        Assert.Equal(fixture.Message.TenantId, entry.TenantId);
        Assert.Equal(Recipient, entry.EmailAddress);
        Assert.Equal(expectedReason, entry.Reason);
        Assert.Null(entry.AddedByUserId);
    }

    [Fact]
    public async Task Handle_does_not_suppress_on_soft_bounce()
    {
        var fixture = await SentMessageInDatabaseAsync();

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.SoftBounced, "evt-soft"));

        Assert.Empty(fixture.Db.SuppressionListEntries);
    }

    [Fact]
    public async Task Handle_does_not_duplicate_an_existing_suppression()
    {
        var fixture = await SentMessageInDatabaseAsync();

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.HardBounced, "evt-a"));
        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Complained, "evt-b"));

        Assert.Single(fixture.Db.SuppressionListEntries);
    }

    private static TranslatedFeedback SignalFor(Fixture fixture, EngagementSignal signal, string eventId) =>
        new(
            null,
            fixture.Message.Id.ToString("N"),
            null,
            Recipient,
            DeliveryFeedback.ForSignal(signal, SentAt.AddMinutes(2), FeedbackSource.Smtp2Go, eventId).Value,
            "{}"
        );

    [Fact]
    public async Task Handle_tells_Notification_when_the_email_was_delivered()
    {
        var fixture = await SentMessageInDatabaseAsync(fromNotification: true);

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Delivered, "evt-del"));

        var delivered = Assert.IsType<PostmasterEmailDeliveredIntegrationEvent>(Assert.Single(fixture.Bus.Published));
        Assert.Equal(NotificationLogId, delivered.NotificationLogId);
        Assert.Equal(DispatchAttemptId, delivered.DispatchAttemptId);
        Assert.Equal(fixture.Message.Id, delivered.SentMessageId);
        Assert.Equal(fixture.Message.TenantId, delivered.TenantId);
    }

    [Theory]
    [InlineData(DeliveryOutcome.HardBounced, "Hard")]
    [InlineData(DeliveryOutcome.SoftBounced, "Soft")]
    [InlineData(DeliveryOutcome.Rejected, "Rejected")]
    public async Task Handle_publishes_the_bounce_that_was_never_published_before(DeliveryOutcome outcome, string type)
    {
        var fixture = await SentMessageInDatabaseAsync(fromNotification: true);

        await fixture.ProcessAsync(OutcomeFor(fixture, outcome, "evt-b"));

        var bounced = Assert.IsType<PostmasterEmailDeliveryBouncedIntegrationEvent>(
            Assert.Single(fixture.Bus.Published)
        );
        Assert.Equal(type, bounced.BounceType);
        Assert.Equal("550", bounced.Reason);
    }

    [Fact]
    public async Task Handle_publishes_a_complaint()
    {
        var fixture = await SentMessageInDatabaseAsync(fromNotification: true);

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Complained, "evt-c"));

        Assert.IsType<PostmasterEmailComplainedIntegrationEvent>(Assert.Single(fixture.Bus.Published));
    }

    [Fact]
    public async Task Handle_publishes_only_when_something_changed()
    {
        var fixture = await SentMessageInDatabaseAsync(fromNotification: true);

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Delivered, "evt-1"));
        // Otro aviso de "entregado" con otro id: se guarda en el historial, pero el estado no cambió.
        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Delivered, "evt-2"));
        // Demorado después de entregado: no retrocede, no se avisa.
        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Deferred, "evt-3"));

        Assert.Single(fixture.Bus.Published);
    }

    [Fact]
    public async Task Handle_publishes_only_the_first_open()
    {
        var fixture = await SentMessageInDatabaseAsync(fromNotification: true);

        await fixture.ProcessAsync(SignalFor(fixture, EngagementSignal.Opened, "open-1"));
        await fixture.ProcessAsync(SignalFor(fixture, EngagementSignal.Opened, "open-2"));
        await fixture.ProcessAsync(SignalFor(fixture, EngagementSignal.Clicked, "click-1"));

        var signals = fixture.Bus.Published.OfType<PostmasterEmailEngagementIntegrationEvent>().Select(e => e.Signal);
        Assert.Equal(["Opened", "Clicked"], signals);
    }

    [Fact]
    public async Task Handle_does_not_publish_for_emails_Notification_did_not_ask_for()
    {
        // Correspondence no tiene log ni intento de Notification: no hay a quién avisar.
        var fixture = await SentMessageInDatabaseAsync(fromNotification: false);

        await fixture.ProcessAsync(OutcomeFor(fixture, DeliveryOutcome.Delivered, "evt-x"));

        Assert.Empty(fixture.Bus.Published);
    }
}
