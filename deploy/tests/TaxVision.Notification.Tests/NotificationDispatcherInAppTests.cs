using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// T3.4 — <see cref="NotificationDispatcher.RecordInAppAsync"/> publica
/// <c>notification.inapp.requested.v1</c> cuando recibe el contexto del hecho, resolviendo
/// categoría/prioridad/acción/recurso desde el catálogo. Sin contexto solo persiste el log.
/// </summary>
public sealed class NotificationDispatcherInAppTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();

    [Fact]
    public async Task RecordInAppAsync_con_contexto_publica_el_evento_con_campos_del_catalogo()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(logs, publisher, uow);

        var sourceEventId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var occurred = new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);

        await dispatcher.RecordInAppAsync(
            TenantId,
            $"user:{RecipientUserId:N}",
            "Overdue: File extension",
            NotificationCategory.Collaboration,
            "task.overdue",
            sourceEventId,
            "corr-123",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksTaskOverdue,
                RecipientUserId,
                sourceEventId,
                "tasks.task_overdue.v1",
                occurred,
                "Overdue: File extension",
                FallbackBody: "Maria Lopez · due today",
                ResourceId: taskId.ToString(),
                ResourceContext: new Dictionary<string, string> { ["customerId"] = customerId.ToString() },
                Params: new Dictionary<string, string> { ["taskTitle"] = "File extension" }
            )
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(TenantId, evt.TenantId);
        Assert.Equal("corr-123", evt.CorrelationId);
        Assert.Equal(RecipientUserId, evt.RecipientUserId);
        Assert.Equal("tasks.task_overdue", evt.Kind);
        Assert.Equal("Work", evt.Category);
        Assert.Equal("High", evt.Priority);
        Assert.Equal("notif.tasks.task_overdue.title", evt.TitleKey);
        Assert.Equal("task", evt.ResourceType);
        Assert.Equal(taskId.ToString(), evt.ResourceId);
        Assert.Equal("open", evt.Action);
        Assert.Equal(sourceEventId, evt.SourceEventId);
        Assert.Equal("tasks.task_overdue.v1", evt.SourceEventType);
        Assert.Equal("Overdue: File extension", evt.FallbackTitle);
        Assert.Equal(customerId.ToString(), evt.ResourceContext["customerId"]);
        Assert.Equal("File extension", evt.Params["taskTitle"]);
        Assert.Equal(DateTimeKind.Utc, evt.OccurredAtUtc.Kind);
        Assert.Single(logs.Logs);
    }

    [Fact]
    public async Task RecordInAppAsync_sin_contexto_no_publica_nada()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(logs, publisher, uow);

        await dispatcher.RecordInAppAsync(
            TenantId,
            $"user:{RecipientUserId:N}",
            "Algo",
            NotificationCategory.Collaboration,
            "task.overdue",
            Guid.NewGuid(),
            "corr-123",
            recipientUserId: RecipientUserId
        );

        Assert.Empty(publisher.Published);
        Assert.Single(logs.Logs);
    }

    [Fact]
    public async Task RecordInAppAsync_mandatory_kind_omite_preferencia_apagada()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(logs, publisher, uow, DisabledPreferences.Instance);

        await dispatcher.RecordInAppAsync(
            TenantId,
            $"user:{RecipientUserId:N}",
            "File quarantined",
            NotificationCategory.StorageAndQuota,
            "cloudstorage.file.infected",
            Guid.NewGuid(),
            "corr-mandatory",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.CloudStorageFileInfected,
                RecipientUserId,
                Guid.NewGuid(),
                "cloudstorage.file.infected.v1",
                DateTime.UtcNow,
                "File quarantined",
                ResourceId: Guid.NewGuid().ToString()
            )
        );

        Assert.Single(publisher.Published);
        Assert.Single(logs.Logs);
    }

    [Fact]
    public async Task RecordInAppAsync_suprimido_por_preferencia_no_publica_ni_loguea()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(logs, publisher, uow, DisabledPreferences.Instance);

        await dispatcher.RecordInAppAsync(
            TenantId,
            $"user:{RecipientUserId:N}",
            "Algo",
            NotificationCategory.Collaboration,
            "task.overdue",
            Guid.NewGuid(),
            "corr-123",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksTaskOverdue,
                RecipientUserId,
                Guid.NewGuid(),
                "tasks.task_overdue.v1",
                DateTime.UtcNow,
                "Algo"
            )
        );

        Assert.Empty(publisher.Published);
        Assert.Empty(logs.Logs);
    }

    private static NotificationDispatcher BuildDispatcher(
        INotificationLogRepository logs,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        IUserNotificationPreferenceRepository? preferences = null
    ) =>
        new(
            NoOpSmsSender.Instance,
            NoOpPushSender.Instance,
            EmptyPushDeviceTokenRepository.Instance,
            logs,
            preferences ?? AlwaysEnabledPreferences.Instance,
            publisher,
            uow,
            new NoOpNotificationDispatchMetrics(),
            NullLogger<NotificationDispatcher>.Instance
        );

    private sealed class RecordingNotificationLogRepository : INotificationLogRepository
    {
        public List<NotificationLog> Logs { get; } = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((Logs, Logs.Count));

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
        public static readonly NoOpSmsSender Instance = new();

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
        public static readonly NoOpPushSender Instance = new();

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class EmptyPushDeviceTokenRepository : IPushDeviceTokenRepository
    {
        public static readonly EmptyPushDeviceTokenRepository Instance = new();

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
        public static readonly AlwaysEnabledPreferences Instance = new();

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

    private sealed class DisabledPreferences : IUserNotificationPreferenceRepository
    {
        public static readonly DisabledPreferences Instance = new();

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(false);

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
