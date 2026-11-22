using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class InAppRejectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SourceEventId = Guid.NewGuid();

    [Fact]
    public async Task Marks_the_log_failed()
    {
        var (logs, log) = SentInAppLog();

        await Handle(logs, Rejected());

        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("rejected_by_communication: InvalidPayload (params.title)", log.Error);
        Assert.Equal(1, logs.Saves);
    }

    [Fact]
    public async Task Rejection_without_kind_does_not_touch_any_log()
    {
        var (logs, log) = SentInAppLog();

        await Handle(logs, Rejected() with { Kind = string.Empty });

        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(0, logs.Saves);
    }

    [Fact]
    public async Task Rejection_from_another_tenant_does_not_touch_the_log()
    {
        var (logs, log) = SentInAppLog();

        await Handle(logs, Rejected() with { TenantId = Guid.NewGuid() });

        Assert.Equal(NotificationStatus.Sent, log.Status);
    }

    private static Task Handle(MemoryLogStatusRepository logs, InAppNotificationRejectedIntegrationEvent evt) =>
        InAppRejectedConsumer.Handle(
            evt,
            logs,
            logs,
            new RecordingDispatchMetrics(),
            new FixedCorrelationContext(),
            NullLogger<InAppNotificationRejectedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static InAppNotificationRejectedIntegrationEvent Rejected() =>
        new()
        {
            TenantId = TenantId,
            SourceEventId = SourceEventId,
            RecipientUserId = UserId,
            Kind = NotificationKinds.TasksTaskOverdue,
            Reason = "InvalidPayload",
            Detail = "params.title",
        };

    private static (MemoryLogStatusRepository Logs, NotificationLog Log) SentInAppLog()
    {
        var log = NotificationLog
            .Create(
                TenantId,
                NotificationChannel.InApp,
                NotificationRecipient.ForUser(UserId),
                "Task overdue",
                "task.overdue",
                SourceEventId,
                correlationId: null,
                kind: NotificationKinds.TasksTaskOverdue
            )
            .Value;
        log.MarkSent();
        var logs = new MemoryLogStatusRepository();
        logs.Logs.Add(log);
        return (logs, log);
    }
}
