using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Postmaster;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Tests unitarios de los 5 consumers de <c>PostmasterOutboundEmailCallbackConsumers.cs</c> (Hardening
/// Fase 19, 2026-07-18) — la contraparte de <c>PostmasterCallbackConsumers.cs</c> para el path de
/// <see cref="TaxVision.Notification.Application.Email.Sending.PostmasterEmailDeliveryService"/>. Cada
/// callback resuelve <c>evt.NotificationLogId</c> contra <see cref="IOutboundEmailRepository"/> (no
/// contra un NotificationLog) y, si lo encuentra, transiciona el <see cref="OutboundEmailMessage"/> y
/// publica <see cref="EmailDeliverySucceededIntegrationEvent"/>/<see cref="EmailDeliveryFailedIntegrationEvent"/>
/// — el mismo contrato que <c>CampaignDeliverySucceededConsumer</c>/<c>CampaignDeliveryFailedConsumer</c>
/// ya escuchan para los contadores de EmailCampaigns.
/// </summary>
public sealed class PostmasterOutboundEmailCallbackConsumersTests
{
    [Fact]
    public async Task SucceededCallback_marks_message_Sent_and_publishes_success_event()
    {
        var campaignId = Guid.NewGuid();
        var message = CreateSendingMessage(campaignId: campaignId);
        var repo = new RecordingOutboundEmailRepository(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliverySucceededIntegrationEvent
        {
            TenantId = message.TenantId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            ProviderMessageId = "postmaster-msg-1",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailSucceededConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(EmailStatus.Sent, message.Status);
        Assert.Equal("Postmaster", message.ProviderType);
        Assert.Equal(1, uow.SaveCount);
        var published = Assert.IsType<EmailDeliverySucceededIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(message.Id, published.MessageId);
        Assert.Equal("Postmaster", published.ProviderType);
        Assert.Equal(campaignId, published.CampaignId);
    }

    [Fact]
    public async Task FailedCallback_marks_message_Failed_and_publishes_failure_event()
    {
        var message = CreateSendingMessage();
        var repo = new RecordingOutboundEmailRepository(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliveryFailedIntegrationEvent
        {
            TenantId = message.TenantId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            Reason = "550 mailbox not found",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailFailedConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Equal("550 mailbox not found", message.Error);
        var published = Assert.IsType<EmailDeliveryFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal("550 mailbox not found", published.Error);
    }

    [Fact]
    public async Task BouncedCallback_marks_message_Bounced_and_publishes_failure_event_for_campaign_counters()
    {
        var message = CreateSendingMessage();
        var repo = new RecordingOutboundEmailRepository(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliveryBouncedIntegrationEvent
        {
            TenantId = message.TenantId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            BounceType = "Permanent",
            Reason = "550 5.1.1 unknown user",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailBouncedConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        // A diferencia de Failed, Bounced usa el estado propio de OutboundEmailMessage (no reachable
        // antes de esta fase salvo por el webhook muerto que se retiró en la misma).
        Assert.Equal(EmailStatus.Bounced, message.Status);
        Assert.NotNull(message.BouncedAtUtc);
        var published = Assert.IsType<EmailDeliveryFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Contains("Permanent", published.Error);
    }

    [Fact]
    public async Task SuppressedCallback_marks_message_Failed_with_suppression_reason()
    {
        var message = CreateSendingMessage();
        var repo = new RecordingOutboundEmailRepository(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliverySuppressedIntegrationEvent
        {
            TenantId = message.TenantId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            SuppressionReason = "Address in suppression list.",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailSuppressedConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Contains("Suppressed", message.Error);
    }

    [Fact]
    public async Task ProviderNotConfiguredCallback_marks_message_Failed()
    {
        var message = CreateSendingMessage();
        var repo = new RecordingOutboundEmailRepository(message);
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent
        {
            TenantId = message.TenantId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailProviderNotConfiguredConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Contains("not configured", message.Error);
    }

    /// <summary>
    /// El callback pertenece al OTRO espacio de ids (un NotificationLog real, del path de
    /// Auth/Signature/Communication) — este consumer no debe encontrar nada ni tocar la BD.
    /// </summary>
    [Fact]
    public async Task Callback_for_unknown_message_id_is_silently_dropped()
    {
        var repo = new RecordingOutboundEmailRepository(); // vacío
        var publisher = new RecordingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliverySucceededIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            NotificationLogId = Guid.NewGuid(), // no es un OutboundEmailMessage real
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterOutboundEmailSucceededConsumer.Handle(
            evt,
            repo,
            publisher,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(publisher.Published);
        Assert.Equal(0, uow.SaveCount);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static OutboundEmailMessage CreateSendingMessage(Guid? campaignId = null)
    {
        var message = OutboundEmailMessage
            .Create(
                Guid.NewGuid(),
                "Subject",
                "<p>Body</p>",
                "Body",
                EmailPriority.Normal,
                [("to@test.com", EmailRecipientKind.To, null)],
                "[]",
                templateId: null,
                templateVersionId: null,
                campaignId: campaignId,
                correlationId: null
            )
            .Value;
        message.MarkSending(); // estado real en el que PostmasterEmailDeliveryService deja el mensaje
        return message;
    }

    private sealed class RecordingOutboundEmailRepository(params OutboundEmailMessage[] seed) : IOutboundEmailRepository
    {
        private readonly Dictionary<Guid, OutboundEmailMessage> _messages = seed.ToDictionary(m => m.Id);

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

    private sealed class NoOpCorrelationContext : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
