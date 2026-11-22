using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Sms;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class SmsDeliveryStatusConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Delivered_marks_the_log_delivered()
    {
        var (logs, log) = SentLog();

        await SmsDeliveryStatusConsumer.Handle(
            Delivered(SmsSourceContext.ForLog(log.Id)),
            logs,
            logs,
            new RecordingDispatchMetrics(),
            new FixedCorrelationContext(),
            NullLogger<SmsMessageDeliveredIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationStatus.Delivered, log.Status);
        Assert.Equal(1, logs.Saves);
    }

    [Fact]
    public async Task Failed_marks_the_log_failed_with_the_provider_code()
    {
        var (logs, log) = SentLog();

        await SmsDeliveryStatusConsumer.Handle(
            new SmsMessageFailedIntegrationEvent
            {
                TenantId = TenantId,
                MessageId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                SourceContext = SmsSourceContext.ForLog(log.Id),
                FailureCode = "Undeliverable",
            },
            logs,
            logs,
            new RecordingDispatchMetrics(),
            new FixedCorrelationContext(),
            NullLogger<SmsMessageFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("Undeliverable", log.Error);
    }

    [Fact]
    public async Task Suppressed_marks_the_log_skipped()
    {
        var (logs, log) = SentLog();

        await SmsDeliveryStatusConsumer.Handle(
            new SmsMessageSuppressedIntegrationEvent
            {
                TenantId = TenantId,
                MessageId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                SourceContext = SmsSourceContext.ForLog(log.Id),
            },
            logs,
            logs,
            new RecordingDispatchMetrics(),
            new FixedCorrelationContext(),
            NullLogger<SmsMessageSuppressedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationStatus.Skipped, log.Status);
        Assert.Equal("suppressed", log.Error);
    }

    [Fact]
    public async Task Foreign_source_context_is_ignored()
    {
        var (logs, log) = SentLog();

        await HandleDelivered(logs, Delivered($"campaign:{log.Id:N}"));

        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(0, logs.Saves);
    }

    [Fact]
    public async Task Log_of_another_tenant_is_not_touched()
    {
        var (logs, log) = SentLog();

        await HandleDelivered(logs, Delivered(SmsSourceContext.ForLog(log.Id)) with { TenantId = Guid.NewGuid() });

        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(0, logs.Saves);
    }

    [Fact]
    public async Task Out_of_order_result_is_ignored()
    {
        var (logs, log) = SentLog();
        log.MarkFailed("Undeliverable");

        await HandleDelivered(logs, Delivered(SmsSourceContext.ForLog(log.Id)));

        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal(0, logs.Saves);
    }

    private static Task HandleDelivered(MemoryLogStatusRepository logs, SmsMessageDeliveredIntegrationEvent evt) =>
        SmsDeliveryStatusConsumer.Handle(
            evt,
            logs,
            logs,
            new RecordingDispatchMetrics(),
            new FixedCorrelationContext(),
            NullLogger<SmsMessageDeliveredIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static SmsMessageDeliveredIntegrationEvent Delivered(string sourceContext) =>
        new()
        {
            TenantId = TenantId,
            MessageId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            SourceContext = sourceContext,
        };

    private static (MemoryLogStatusRepository Logs, NotificationLog Log) SentLog()
    {
        var log = NotificationLog
            .Create(
                TenantId,
                NotificationChannel.Sms,
                NotificationRecipient.ForPhone("+15550001111").Value,
                "auth.otp",
                "auth.otp",
                Guid.NewGuid(),
                correlationId: null
            )
            .Value;
        log.MarkSent();
        var logs = new MemoryLogStatusRepository();
        logs.Logs.Add(log);
        return (logs, log);
    }
}
