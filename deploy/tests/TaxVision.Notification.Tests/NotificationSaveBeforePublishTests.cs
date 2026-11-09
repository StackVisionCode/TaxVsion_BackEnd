using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Application.Email.Sending.Commands;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Notification.Tests;

/// <summary>
/// BB-16: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea. El
/// guardado falla como un timeout de SQL y no debe quedar nada en el bus.
/// </summary>
public sealed class NotificationSaveBeforePublishTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gateway_WhenSaveFails_PublishesNothing(bool confidential)
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var gateway = new EventBasedEmailDispatchGateway(
            publisher,
            new EmptyLogs(),
            new FailingUnitOfWork(),
            TestDeliveryGates.Open(),
            NullLogger<EventBasedEmailDispatchGateway>.Instance
        );
        var request = new EmailDispatchRequest(
            TenantId: Guid.NewGuid(),
            To: "customer@test.com",
            Subject: "Subject",
            HtmlBody: "<p>Hi</p>",
            TextBody: "Hi",
            TemplateKey: "auth.welcome",
            RelatedEventId: Guid.NewGuid(),
            CorrelationId: "corr",
            Confidential: confidential
        );

        await Assert.ThrowsAsync<TimeoutException>(() => gateway.QueueEmailAsync(request, CancellationToken.None));

        Assert.Empty(publisher.Published);
        Assert.Empty(publisher.SentToQueue);
    }

    [Fact]
    public async Task Dispatcher_InApp_WhenSaveFails_PublishesNothing()
    {
        var harness = new NotificationDispatcherHarness(unitOfWork: new FailingUnitOfWork());
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            harness.Dispatcher.RecordInAppAsync(
                Guid.NewGuid(),
                NotificationRecipient.ForUser(userId),
                "Overdue",
                NotificationCategory.Collaboration,
                "task.overdue",
                Guid.NewGuid(),
                "corr",
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksTaskOverdue,
                    userId,
                    Guid.NewGuid(),
                    "tasks.task_overdue.v1",
                    DateTime.UtcNow,
                    "Overdue"
                )
            )
        );

        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task PostmasterDelivery_WhenSaveFails_PublishesNothing()
    {
        var message = Message();
        var publisher = new CapturingIntegrationEventPublisher();
        var service = new PostmasterEmailDeliveryService(
            new OneMessageRepository(message),
            publisher,
            new FixedCorrelation(),
            new FailingUnitOfWork()
        );

        await Assert.ThrowsAsync<TimeoutException>(() => service.DeliverAsync(message.Id, CancellationToken.None));

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task SendEmail_WhenSaveFails_PublishesNothing()
    {
        var bus = DispatchProxy.Create<IMessageBus, RecordingBusProxy>();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            SendEmailHandler.Handle(
                new SendEmailCommand(
                    Guid.NewGuid(),
                    "Subject",
                    "<p>Hi</p>",
                    "Hi",
                    EmailPriority.Normal,
                    [new EmailRecipientInput("to@test.com", EmailRecipientKind.To, null)],
                    null
                ),
                new OneMessageRepository(),
                bus,
                new FixedCorrelation(),
                new FailingUnitOfWork(),
                CancellationToken.None
            )
        );

        Assert.Empty(((RecordingBusProxy)(object)bus).Published);
    }

    private static OutboundEmailMessage Message() =>
        OutboundEmailMessage
            .Create(
                Guid.NewGuid(),
                "Subject",
                "<p>Hi</p>",
                "Hi",
                EmailPriority.Normal,
                [("to@test.com", EmailRecipientKind.To, null)],
                "[]",
                null,
                null,
                null,
                null
            )
            .Value;

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new TimeoutException("SQL timeout");
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => throw new NotSupportedException();
    }

    private sealed class EmptyLogs : INotificationLogRepository
    {
        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task AddAsync(NotificationLog log, CancellationToken ct = default) => Task.CompletedTask;

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>(([], 0));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class OneMessageRepository(OutboundEmailMessage? message = null) : IOutboundEmailRepository
    {
        public Task AddAsync(OutboundEmailMessage added, CancellationToken ct = default) => Task.CompletedTask;

        public Task<OutboundEmailMessage?> GetForDeliveryAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult(message);

        public Task<OutboundEmailMessage?> GetByIdAsync(
            Guid messageId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(message);

        public Task<(IReadOnlyList<OutboundEmailMessage> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            EmailStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<OutboundEmailMessage>, int)>(([], 0));
    }
}

/// <summary>Bus de prueba: guarda lo publicado y rechaza cualquier otra llamada.</summary>
internal class RecordingBusProxy : DispatchProxy
{
    public List<object> Published { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMessageBus.PublishAsync))
            throw new NotSupportedException(targetMethod?.Name);
        Published.Add(args![0]!);
        return ValueTask.CompletedTask;
    }
}
