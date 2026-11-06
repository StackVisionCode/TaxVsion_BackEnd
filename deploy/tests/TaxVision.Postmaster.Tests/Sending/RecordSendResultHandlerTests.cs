using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Sending;

/// <summary>PMS-07: cuenta lo emitido por comando (cascada + bus). Con los dos caminos daría 2.</summary>
public sealed class RecordSendResultHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid LogId = Guid.NewGuid();
    private static readonly Guid AttemptId = Guid.NewGuid();
    private static readonly Guid CampaignId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeSentMessageRepository _sentMessages = new();
    private readonly FakeMessageBus _bus = new();

    private async Task<SentMessage> SeedSendingAsync(Guid? notificationLogId = null, Guid? dispatchAttemptId = null)
    {
        var message = SentMessage
            .Queue(
                Tenant,
                $"key:{Guid.NewGuid():N}",
                "Subject",
                "no-reply@example.test",
                EmailStream.Transactional,
                "system-smtp",
                notificationLogId,
                "corr-1",
                null,
                null,
                null,
                Now,
                campaignId: CampaignId,
                dispatchAttemptId: dispatchAttemptId
            )
            .Value;
        message.AddRecipient("customer@example.test", RecipientType.To, null);
        message.MarkAsSending();
        await _sentMessages.AddAsync(message);
        return message;
    }

    private async Task RecordAsync(SentMessage message, SendResult result)
    {
        await RecordSendResultHandler.Handle(
            new RecordSendResultCommand(message.Id, result, "Unknown failure.", null),
            _sentMessages,
            new FakeIdempotencyGuard(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(_bus, _sentMessages);
    }

    [Fact]
    public async Task Each_result_emits_exactly_one_integration_event_Sent()
    {
        var message = await SeedSendingAsync(LogId, AttemptId);

        await RecordAsync(message, new SendResult(true, "message-id", null, [], ProviderReference: "provider-ref"));

        var notice = Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(Assert.Single(_bus.Published));
        Assert.Equal(Tenant, notice.TenantId);
        Assert.Equal("corr-1", notice.CorrelationId);
        Assert.Equal(LogId, notice.NotificationLogId);
        Assert.Equal(AttemptId, notice.DispatchAttemptId);
        Assert.Equal(message.Id, notice.SentMessageId);
        // Notification recibe el Message-ID, no la referencia interna del proveedor (como antes).
        Assert.Equal("message-id", notice.ProviderMessageId);
        Assert.Equal(CampaignId, notice.CampaignId);
    }

    [Fact]
    public async Task Each_result_emits_exactly_one_integration_event_Failed()
    {
        var message = await SeedSendingAsync(LogId, AttemptId);
        var reason = new string('x', 600);

        await RecordAsync(message, new SendResult(false, "message-id", reason, []));

        var notice = Assert.IsType<PostmasterEmailDeliveryFailedIntegrationEvent>(Assert.Single(_bus.Published));
        // El motivo viaja completo; el agregado lo recorta solo para guardarlo.
        Assert.Equal(reason, notice.Reason);
        Assert.Equal("message-id", notice.ProviderMessageId);
    }

    [Fact]
    public async Task Failure_without_a_reason_uses_the_fallback_reason()
    {
        var message = await SeedSendingAsync(LogId, AttemptId);

        await RecordAsync(message, new SendResult(false, null, null, []));

        var notice = Assert.IsType<PostmasterEmailDeliveryFailedIntegrationEvent>(Assert.Single(_bus.Published));
        Assert.Equal("Unknown failure.", notice.Reason);
    }

    [Fact]
    public async Task A_result_already_recorded_emits_nothing_more()
    {
        var message = await SeedSendingAsync(LogId, AttemptId);
        await RecordAsync(message, new SendResult(true, "message-id", null, []));
        _bus.Published.Clear();

        await RecordAsync(message, new SendResult(true, "message-id", null, []));

        Assert.Empty(_bus.Published);
    }

    // Correspondence lee la respuesta HTTP: no hay a quién avisar.
    [Fact]
    public async Task A_message_Notification_did_not_ask_for_emits_nothing()
    {
        var message = await SeedSendingAsync();

        await RecordAsync(message, new SendResult(true, "message-id", null, []));

        Assert.Equal(SentMessageStatus.Sent, message.Status);
        Assert.Empty(_bus.Published);
    }
}
