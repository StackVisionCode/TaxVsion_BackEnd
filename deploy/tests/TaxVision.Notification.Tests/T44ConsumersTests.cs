using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>T4.4 — consumers nuevos en Notification (sin tocar servicios ajenos): TaskAssigned y correo de cliente.</summary>
public sealed class T44ConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task TaskAssigned_publica_in_app_al_asignado()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);
        var assignee = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await TaskAssignedConsumer.Handle(
            new TaskAssignedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                TaskId = taskId,
                Title = "File the 1040",
                AssigneeUserId = assignee,
                DueAtUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
            },
            dispatcher,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal("tasks.task_assigned", evt.Kind);
        Assert.Equal(assignee, evt.RecipientUserId);
        Assert.Equal(taskId.ToString(), evt.ResourceId);
    }

    [Fact]
    public async Task TaskAssigned_auto_asignacion_no_notifica_al_actor()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);
        var actor = Guid.NewGuid();

        await TaskAssignedConsumer.Handle(
            new TaskAssignedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                TaskId = Guid.NewGuid(),
                Title = "Review 1040",
                AssigneeUserId = actor,
                AssignedByUserId = actor,
            },
            dispatcher,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task TaskAssigned_sin_asignado_no_publica()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);

        await TaskAssignedConsumer.Handle(
            new TaskAssignedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                TaskId = Guid.NewGuid(),
                Title = "Orphan",
                AssigneeUserId = Guid.Empty,
            },
            dispatcher,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task CustomerEmail_buzon_personal_notifica_al_dueno_con_groupkey_del_hilo()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);
        var owner = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var resolver = new FakeResolver(Result.Success<IReadOnlyList<Guid>>([owner]));

        await CorrespondenceCustomerEmailReceivedConsumer.Handle(
            new CorrespondenceCustomerEmailReceivedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                CustomerId = Guid.NewGuid(),
                IncomingEmailId = Guid.NewGuid(),
                EmailThreadId = threadId,
                Subject = "Question about my return",
                AccountId = Guid.NewGuid(),
                MailboxOwnerUserId = owner,
            },
            dispatcher,
            resolver,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(owner, evt.RecipientUserId);
        Assert.Equal($"email_thread:{threadId}", evt.GroupKey);
        Assert.Equal("correspondence.customer_email_received", evt.Kind);
    }

    [Fact]
    public async Task CustomerEmail_sin_account_id_no_hace_nada()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);
        var resolver = new FakeResolver(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));

        await CorrespondenceCustomerEmailReceivedConsumer.Handle(
            new CorrespondenceCustomerEmailReceivedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                CustomerId = Guid.NewGuid(),
                IncomingEmailId = Guid.NewGuid(),
                EmailThreadId = Guid.NewGuid(),
                Subject = "Legacy event",
                AccountId = null,
            },
            dispatcher,
            resolver,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task CustomerEmail_sin_destinatario_valido_no_publica()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(publisher);
        var resolver = new FakeResolver(
            Result.Failure<IReadOnlyList<Guid>>(new Error("Notification.Audience.NoRecipient", "none"))
        );

        await CorrespondenceCustomerEmailReceivedConsumer.Handle(
            new CorrespondenceCustomerEmailReceivedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                CustomerId = Guid.NewGuid(),
                IncomingEmailId = Guid.NewGuid(),
                EmailThreadId = Guid.NewGuid(),
                Subject = "Office mailbox, no preparer",
                AccountId = Guid.NewGuid(),
                MailboxOwnerUserId = null,
            },
            dispatcher,
            resolver,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(publisher.Published);
    }

    private static NotificationDispatcher BuildDispatcher(IIntegrationEventPublisher publisher) =>
        new(
            new NoOpSmsSender(),
            new NoOpPushSender(),
            new NoOpPushDeviceTokenRepository(),
            new NoOpNotificationLogRepository(),
            new AlwaysEnabledPreferences(),
            publisher,
            new NoOpUnitOfWork(),
            new NoOpNotificationDispatchMetrics(),
            NullLogger<NotificationDispatcher>.Instance
        );

    private sealed class FakeResolver(Result<IReadOnlyList<Guid>> result) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(result);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NoOpNotificationLogRepository : INotificationLogRepository
    {
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

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken
        ) => Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushDeviceTokenRepository : IPushDeviceTokenRepository
    {
        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PushDeviceToken>>([]);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AlwaysEnabledPreferences : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(true);

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
