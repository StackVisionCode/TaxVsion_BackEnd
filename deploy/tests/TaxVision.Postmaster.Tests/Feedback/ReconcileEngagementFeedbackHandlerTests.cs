using System.Net;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileEngagementFeedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;
using static TaxVision.Postmaster.Tests.Feedback.FakeSmtp2GoHttpHandler;

namespace TaxVision.Postmaster.Tests.Feedback;

/// <summary>Aperturas y clics por rango: poller real con la API falsa, repositorios reales (EF InMemory).</summary>
public sealed class ReconcileEngagementFeedbackHandlerTests
{
    private const string OurEmailId = "1oQsWu-4XUKtC-0n";
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static PostmasterDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private static async Task<SentMessage> AddSentAsync(PostmasterDbContext db, string providerReference = OurEmailId)
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
                Now.AddHours(-12),
                deliveryLane: DeliveryLane.SystemProvider
            )
            .Value;
        message.AddRecipient("customer@example.com", RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent(providerReference, Now.AddHours(-12));
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return message;
    }

    private static DeliveryFeedbackCapabilities Capabilities(FakeSmtp2GoHttpHandler? api) =>
        new(
            [new Smtp2GoWebhookFeedbackTranslator(NullLogger<Smtp2GoWebhookFeedbackTranslator>.Instance)],
            [],
            api is null ? [] : [Smtp2GoActivityFeedbackPollerTests.CreatePoller(api)]
        );

    private static Task<DeliveryFeedbackTally> ReconcileAsync(PostmasterDbContext db, FakeSmtp2GoHttpHandler? api) =>
        ReconcileEngagementFeedbackHandler.Handle(
            new ReconcileEngagementFeedbackCommand(Now.AddHours(-48), Now, 2000, Now),
            Capabilities(api),
            new SentMessageRepository(db),
            new DeliveryFeedbackReceiptRepository(db),
            new UnmatchedDeliveryFeedbackRepository(db),
            new SuppressionListRepository(db),
            db,
            new FakeMessageBus(),
            NullLogger.Instance,
            CancellationToken.None
        );

    private static SentMessageRecipient LoadRecipient(PostmasterDbContext db, SentMessage message) =>
        db.SentMessageRecipients.IgnoreQueryFilters().AsNoTracking().Single(r => r.SentMessageId == message.Id);

    [Fact]
    public async Task Handle_applies_opens_and_clicks_of_our_messages()
    {
        await using var db = CreateContext();
        var message = await AddSentAsync(db);
        var api = new FakeSmtp2GoHttpHandler();
        api.Enqueue(
            HttpStatusCode.OK,
            Page(null, ActivityEvent(OurEmailId, "opened"), ActivityEvent(OurEmailId, "clicked"))
        );

        var tally = await ReconcileAsync(db, api);

        Assert.Equal(2, tally.Applied);
        var recipient = LoadRecipient(db, message);
        Assert.Equal(1, recipient.OpenCount);
        Assert.Equal(1, recipient.ClickCount);
    }

    [Fact]
    public async Task Handle_drops_events_of_other_senders_without_recording_them_as_unmatched()
    {
        await using var db = CreateContext();
        await AddSentAsync(db);
        var api = new FakeSmtp2GoHttpHandler();
        // Otro entorno que comparte la cuenta de SMTP2GO.
        api.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent("someone-else", "opened")));

        var tally = await ReconcileAsync(db, api);

        Assert.Equal(DeliveryFeedbackTally.Empty, tally);
        Assert.Empty(db.UnmatchedDeliveryFeedback.IgnoreQueryFilters());
        Assert.Empty(db.DeliveryFeedbackReceipts.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Handle_does_not_count_twice_an_open_that_already_came_by_webhook()
    {
        await using var db = CreateContext();
        var message = await AddSentAsync(db);
        var webhook = $$"""
            {"event":"open","time":"2026-10-21T06:01:00Z","rcpt":"customer@example.com","email_id":"{{OurEmailId}}"}
            """;
        await ProcessDeliveryFeedbackHandler.Handle(
            new ProcessDeliveryFeedbackCommand("Smtp2Go", webhook, Now.AddHours(-1)),
            Capabilities(api: null),
            new SentMessageRepository(db),
            new DeliveryFeedbackReceiptRepository(db),
            new UnmatchedDeliveryFeedbackRepository(db),
            new SuppressionListRepository(db),
            db,
            new FakeMessageBus(),
            NullLogger.Instance,
            CancellationToken.None
        );
        var api = new FakeSmtp2GoHttpHandler();
        api.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent(OurEmailId, "opened", date: "2026-10-21T06:01:00Z")));

        var tally = await ReconcileAsync(db, api);

        Assert.Equal(0, tally.Applied);
        Assert.Equal(1, tally.Duplicates);
        Assert.Equal(1, LoadRecipient(db, message).OpenCount);
    }

    [Fact]
    public async Task Handle_rereading_the_same_window_is_a_duplicate()
    {
        await using var db = CreateContext();
        var message = await AddSentAsync(db);
        var api = new FakeSmtp2GoHttpHandler();
        api.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent(OurEmailId, "opened")));
        api.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent(OurEmailId, "opened")));

        await ReconcileAsync(db, api);
        var second = await ReconcileAsync(db, api);

        Assert.Equal(1, second.Duplicates);
        Assert.Equal(1, LoadRecipient(db, message).OpenCount);
    }

    [Fact]
    public async Task Handle_does_nothing_without_an_engagement_poller()
    {
        await using var db = CreateContext();
        await AddSentAsync(db);

        var tally = await ReconcileAsync(db, api: null);

        Assert.Equal(DeliveryFeedbackTally.Empty, tally);
    }
}
