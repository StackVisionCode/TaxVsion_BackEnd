using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Tests unitarios de <see cref="PostmasterEmailDeliveryService"/> (Hardening Fase 19, 2026-07-18) — la
/// implementación de <see cref="IEmailDeliveryService"/> registrada cuando
/// <c>Notification:UsePostmasterDispatch=true</c>. Verifica que publica
/// <see cref="NotificationsEmailSendRequestedIntegrationEvent"/> reusando
/// <c>OutboundEmailMessage.Id</c> como <c>NotificationLogId</c> (correlación opaca, no un NotificationLog
/// real — ver el comentario de clase de <see cref="PostmasterEmailDeliveryService"/>), deja el mensaje en
/// <see cref="EmailStatus.Sending"/> (el callback lo termina, no esta clase), y mapea correctamente
/// destinatarios/scope/stream.
/// </summary>
public sealed class PostmasterEmailDeliveryServiceTests
{
    [Fact]
    public async Task DeliverAsync_message_not_found_returns_failure()
    {
        var repo = new RecordingOutboundEmailRepository();
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var service = new PostmasterEmailDeliveryService(repo, publisher, new NoOpCorrelationContext(), uow);

        var result = await service.DeliverAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailMessage.NotFound", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task DeliverAsync_already_sent_message_is_a_noop()
    {
        var message = CreateMessage(recipients: [("to@test.com", EmailRecipientKind.To, null)]);
        message.MarkSending();
        message.MarkSent("Smtp", null); // ya no CanDeliver()

        var repo = new RecordingOutboundEmailRepository();
        repo.Seed(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var service = new PostmasterEmailDeliveryService(repo, publisher, new NoOpCorrelationContext(), uow);

        var result = await service.DeliverAsync(message.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(publisher.Published);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task DeliverAsync_publishes_request_event_and_marks_message_Sending()
    {
        var tenantId = Guid.NewGuid();
        var message = CreateMessage(
            tenantId,
            recipients: [("to@test.com", EmailRecipientKind.To, "Customer")],
            correlationId: "corr-1"
        );

        var repo = new RecordingOutboundEmailRepository();
        repo.Seed(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var correlation = new NoOpCorrelationContext("corr-1");
        var service = new PostmasterEmailDeliveryService(repo, publisher, correlation, uow);

        var result = await service.DeliverAsync(message.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailStatus.Sending, message.Status);
        var evt = Assert.IsType<NotificationsEmailSendRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        // NotificationLogId es una clave opaca reusada como MessageId, NO un NotificationLog real —
        // ver el comentario de clase de PostmasterEmailDeliveryService.
        Assert.Equal(message.Id, evt.NotificationLogId);
        Assert.Equal(message.Id.ToString("N"), evt.IdempotencyKey);
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal("corr-1", evt.CorrelationId);
        Assert.Equal("to@test.com", evt.To);
        Assert.Equal("Tenant", evt.RequiredProviderScope);
        Assert.Equal("Tenant", evt.LogoScope);
        Assert.Equal("Transactional", evt.Stream);
        Assert.Null(evt.Cc);
        Assert.Null(evt.Bcc);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task DeliverAsync_campaign_message_uses_Bulk_stream()
    {
        var campaignId = Guid.NewGuid();
        var message = CreateMessage(recipients: [("to@test.com", EmailRecipientKind.To, null)], campaignId: campaignId);

        var repo = new RecordingOutboundEmailRepository();
        repo.Seed(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var service = new PostmasterEmailDeliveryService(
            repo,
            publisher,
            new NoOpCorrelationContext(),
            new NoOpUnitOfWork()
        );

        await service.DeliverAsync(message.Id, CancellationToken.None);

        var evt = Assert.IsType<NotificationsEmailSendRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal("Bulk", evt.Stream);
        Assert.Equal("notification.campaign_email", evt.TemplateKey);
    }

    /// <summary>
    /// notifications.email_send_requested.v1 solo tiene un campo `To`. Con más de un destinatario "To"
    /// (posible solo vía el endpoint público genérico, no vía plantillas/campañas), el primero va como
    /// `To` y el resto se suma a `Cc` — ver el comentario de clase de PostmasterEmailDeliveryService.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_extra_To_recipients_merge_into_Cc()
    {
        var message = CreateMessage(
            recipients:
            [
                ("primary@test.com", EmailRecipientKind.To, null),
                ("secondary@test.com", EmailRecipientKind.To, null),
                ("cc1@test.com", EmailRecipientKind.Cc, null),
                ("bcc1@test.com", EmailRecipientKind.Bcc, null),
            ]
        );

        var repo = new RecordingOutboundEmailRepository();
        repo.Seed(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var service = new PostmasterEmailDeliveryService(
            repo,
            publisher,
            new NoOpCorrelationContext(),
            new NoOpUnitOfWork()
        );

        await service.DeliverAsync(message.Id, CancellationToken.None);

        var evt = Assert.IsType<NotificationsEmailSendRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal("primary@test.com", evt.To);
        Assert.NotNull(evt.Cc);
        Assert.Contains("cc1@test.com", evt.Cc!);
        Assert.Contains("secondary@test.com", evt.Cc!);
        Assert.Equal(2, evt.Cc!.Count);
        Assert.NotNull(evt.Bcc);
        Assert.Equal(["bcc1@test.com"], evt.Bcc!);
    }

    // ------------------------------------------------------------------
    // Helpers (mismo patrón que EventBasedEmailDispatchGatewayTests: fakes locales de mano, sin Moq).
    // ------------------------------------------------------------------

    private static OutboundEmailMessage CreateMessage(
        Guid? tenantId = null,
        IReadOnlyList<(string Address, EmailRecipientKind Kind, string? Name)>? recipients = null,
        Guid? campaignId = null,
        string? correlationId = null
    ) =>
        OutboundEmailMessage
            .Create(
                tenantId ?? Guid.NewGuid(),
                "Subject",
                "<p>Body</p>",
                "Body",
                EmailPriority.Normal,
                recipients ?? [("to@test.com", EmailRecipientKind.To, null)],
                "[]",
                templateId: null,
                templateVersionId: null,
                campaignId: campaignId,
                correlationId: correlationId
            )
            .Value;

    private sealed class RecordingOutboundEmailRepository : IOutboundEmailRepository
    {
        private readonly Dictionary<Guid, OutboundEmailMessage> _messages = new();

        public void Seed(OutboundEmailMessage message) => _messages[message.Id] = message;

        public Task AddAsync(OutboundEmailMessage message, CancellationToken ct = default)
        {
            _messages[message.Id] = message;
            return Task.CompletedTask;
        }

        public Task<OutboundEmailMessage?> GetForDeliveryAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult(_messages.GetValueOrDefault(messageId));

        public Task<OutboundEmailMessage?> GetByIdAsync(
            Guid messageId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(_messages.GetValueOrDefault(messageId));

        public Task<(IReadOnlyList<OutboundEmailMessage> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            EmailStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<OutboundEmailMessage>, int)>(([], 0));
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpCorrelationContext(string correlationId = "test")
        : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => correlationId;

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
