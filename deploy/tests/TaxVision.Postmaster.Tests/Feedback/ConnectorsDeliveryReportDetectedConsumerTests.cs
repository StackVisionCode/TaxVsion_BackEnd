using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Application.Feedback.Consumers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Feedback.Dsn;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Feedback;

/// <summary>Un rebote que llegó al buzón de la oficina, de punta a punta contra el repositorio real.</summary>
public sealed class ConnectorsDeliveryReportDetectedConsumerTests
{
    private const string Recipient = "gone@example.com";
    private static readonly DateTime SentAt = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid MailboxAccountId = Guid.NewGuid();

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

    /// <summary>Enviado por el buzón (Graph): el Message-ID que quedó no es el nuestro.</summary>
    private static async Task<SentMessage> SentThroughMailboxAsync(PostmasterDbContext db, Guid? draftId = null)
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Your documents",
                "office@contoso.com",
                EmailStream.Transactional,
                "Graph",
                null,
                null,
                null,
                null,
                null,
                SentAt,
                correspondenceDraftId: draftId,
                deliveryLane: DeliveryLane.TenantMailbox
            )
            .Value;
        message.AddRecipient(Recipient, RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent("draft-1", SentAt);
        message.RecordInternetMessageId("<AM6P@outlook.com>");
        await db.SentMessages.AddAsync(message);
        await db.SaveChangesAsync();
        return message;
    }

    private static ConnectorsDeliveryReportDetectedIntegrationEvent Report(
        Guid tenantId,
        string statusCode = "5.1.1",
        string diagnostic = "550 5.1.1 user unknown"
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = "corr-1",
            AccountId = MailboxAccountId,
            ReportProviderMessageId = "report-1",
            OriginalInternetMessageId = "AM6P@outlook.com",
            Recipient = Recipient,
            Action = "failed",
            StatusCode = statusCode,
            DiagnosticSnippet = diagnostic,
            ReportedAtUtc = SentAt.AddMinutes(2),
        };

    private static Task HandleAsync(
        PostmasterDbContext db,
        ConnectorsDeliveryReportDetectedIntegrationEvent evt,
        FakeMessageBus? bus = null
    ) =>
        ConnectorsDeliveryReportDetectedConsumer.Handle(
            evt,
            new DeliveryFeedbackCapabilities([new DsnFeedbackTranslator()], []),
            new SentMessageRepository(db),
            new DeliveryFeedbackReceiptRepository(db),
            new UnmatchedDeliveryFeedbackRepository(db),
            new SuppressionListRepository(db),
            db,
            bus ?? new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

    private static SentMessageRecipient LoadRecipient(PostmasterDbContext db, Guid messageId) =>
        db.SentMessageRecipients.IgnoreQueryFilters().Single(r => r.SentMessageId == messageId);

    [Fact]
    public async Task A_bounce_in_the_office_mailbox_marks_the_email_and_suppresses_the_address()
    {
        await using var db = CreateContext();
        var message = await SentThroughMailboxAsync(db);

        await HandleAsync(db, Report(message.TenantId));

        // Graph cambió nuestro Message-ID: se encontró por el que quedó.
        var recipient = LoadRecipient(db, message.Id);
        Assert.Equal(DeliveryOutcome.HardBounced, recipient.Outcome);
        Assert.Equal("550 5.1.1 user unknown", recipient.ErrorReason);
        Assert.Single(db.SuppressionListEntries);
    }

    [Fact]
    public async Task A_report_read_twice_is_applied_once()
    {
        await using var db = CreateContext();
        var message = await SentThroughMailboxAsync(db);

        await HandleAsync(db, Report(message.TenantId));
        await HandleAsync(db, Report(message.TenantId));

        Assert.Single(db.DeliveryFeedbackReceipts);
    }

    [Fact]
    public async Task A_report_in_another_offices_mailbox_cannot_touch_the_email()
    {
        await using var db = CreateContext();
        var message = await SentThroughMailboxAsync(db);

        // Otra oficina fabrica en su buzón un aviso que cita el Message-ID de este correo.
        await HandleAsync(db, Report(Guid.NewGuid()));

        Assert.Equal(DeliveryOutcome.Accepted, LoadRecipient(db, message.Id).Outcome);
        Assert.Empty(db.SuppressionListEntries);
        Assert.Single(db.UnmatchedDeliveryFeedback);
    }

    [Fact]
    public async Task A_bounce_of_an_email_from_the_CRM_reaches_Correspondence_with_the_report_location()
    {
        await using var db = CreateContext();
        var draftId = Guid.NewGuid();
        var message = await SentThroughMailboxAsync(db, draftId);
        var bus = new FakeMessageBus();

        await HandleAsync(db, Report(message.TenantId), bus);
        // El mismo aviso leído otra vez no se vuelve a publicar.
        await HandleAsync(db, Report(message.TenantId), bus);

        var evt = Assert.IsType<PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(draftId, evt.CorrespondenceDraftId);
        Assert.Equal(CorrespondenceDeliveryOutcomes.NotDelivered, evt.Outcome);
        Assert.Equal(DeliveryFailureCategories.InvalidAddress, evt.Category);
        Assert.Equal("5.1.1", evt.StatusCode);
        Assert.True(evt.Suppressed);
        Assert.Equal(MailboxAccountId, evt.ReportAccountId);
        Assert.Equal("report-1", evt.ReportProviderMessageId);
    }

    [Fact]
    public async Task A_full_mailbox_report_is_temporary_and_does_not_block_the_address()
    {
        await using var db = CreateContext();
        var message = await SentThroughMailboxAsync(db, Guid.NewGuid());
        var bus = new FakeMessageBus();

        await HandleAsync(db, Report(message.TenantId, "5.2.2", "552 5.2.2 mailbox full"), bus);

        Assert.Equal(DeliveryOutcome.SoftBounced, LoadRecipient(db, message.Id).Outcome);
        Assert.Empty(db.SuppressionListEntries);
        var evt = Assert.IsType<PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(DeliveryFailureCategories.MailboxFull, evt.Category);
        Assert.False(evt.Suppressed);
    }
}
