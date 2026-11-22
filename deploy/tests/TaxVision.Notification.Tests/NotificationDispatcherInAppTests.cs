using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Domain.Recipients;
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
            NotificationRecipient.ForUser(RecipientUserId),
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
            NotificationRecipient.ForUser(RecipientUserId),
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
    public async Task Kind_outside_the_catalog_is_recorded_as_failed()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(logs, publisher, new NoOpUnitOfWork());

        await dispatcher.RecordInAppAsync(
            TenantId,
            NotificationRecipient.ForUser(RecipientUserId),
            "Something",
            NotificationCategory.Collaboration,
            "unknown.kind",
            Guid.NewGuid(),
            "corr-unknown",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                "unknown.kind",
                RecipientUserId,
                Guid.NewGuid(),
                "unknown.kind.v1",
                DateTime.UtcNow,
                "Something"
            )
        );

        Assert.Empty(publisher.Published);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("kind_not_in_catalog", log.Error);
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
            NotificationRecipient.ForUser(RecipientUserId),
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
            NotificationRecipient.ForUser(RecipientUserId),
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

    [Fact]
    public async Task RecordInAppAsync_kind_fuera_del_catalogo_guarda_el_log_y_no_publica()
    {
        var logs = new RecordingNotificationLogRepository();
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(logs, publisher, new NoOpUnitOfWork());

        await dispatcher.RecordInAppAsync(
            TenantId,
            NotificationRecipient.ForUser(RecipientUserId),
            "Algo",
            NotificationCategory.Collaboration,
            "task.overdue",
            Guid.NewGuid(),
            "corr-123",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                "kind.no.existe",
                RecipientUserId,
                Guid.NewGuid(),
                "x.v1",
                DateTime.UtcNow,
                "Algo"
            )
        );

        Assert.Empty(publisher.Published);
        Assert.Single(logs.Logs);
    }

    [Fact]
    public async Task RecordInAppAsync_recorta_a_los_limites_del_consumer_node()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(new RecordingNotificationLogRepository(), publisher, new NoOpUnitOfWork());

        await dispatcher.RecordInAppAsync(
            TenantId,
            NotificationRecipient.ForUser(RecipientUserId),
            "Algo",
            NotificationCategory.Collaboration,
            "task.overdue",
            Guid.NewGuid(),
            new string('c', 60),
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksTaskOverdue,
                RecipientUserId,
                Guid.NewGuid(),
                new string('s', 200),
                DateTime.UtcNow,
                new string('t', 300),
                FallbackBody: $"  {new string('b', 1200)}  ",
                ResourceId: new string('r', 200),
                GroupKey: new string('g', 300)
            )
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(60, evt.CorrelationId.Length);
        Assert.Equal(128, evt.SourceEventType.Length);
        Assert.Equal(200, evt.FallbackTitle.Length);
        Assert.Equal(new string('b', 1000), evt.FallbackBody);
        Assert.Equal(128, evt.ResourceId!.Length);
        Assert.Equal(256, evt.GroupKey!.Length);
    }

    [Fact]
    public async Task RecordInAppAsync_cuerpo_en_blanco_viaja_nulo()
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(new RecordingNotificationLogRepository(), publisher, new NoOpUnitOfWork());

        await dispatcher.RecordInAppAsync(
            TenantId,
            NotificationRecipient.ForUser(RecipientUserId),
            "Algo",
            NotificationCategory.Collaboration,
            "task.overdue",
            Guid.NewGuid(),
            null,
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TasksTaskOverdue,
                RecipientUserId,
                Guid.NewGuid(),
                "tasks.task_overdue.v1",
                DateTime.UtcNow,
                "Algo",
                FallbackBody: "   "
            )
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Null(evt.FallbackBody);
        Assert.Equal(string.Empty, evt.CorrelationId);
    }

    private static NotificationDispatcher BuildDispatcher(
        INotificationLogRepository logs,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        IUserNotificationPreferenceRepository? preferences = null,
        InMemoryUserEmailDirectoryRepository? directory = null,
        TestPortalAccounts? portalAccounts = null
    )
    {
        directory ??= new InMemoryUserEmailDirectoryRepository();
        return TestDispatchers.Create(
            NoOpSmsSender.Instance,
            NoOpPushSender.Instance,
            EmptyPushDeviceTokenRepository.Instance,
            logs,
            TestDeliveryGates.For(preferences ?? AlwaysEnabledPreferences.Instance, directory),
            publisher,
            uow,
            new NoOpNotificationDispatchMetrics(),
            new TaxVision.Notification.Tests.TestDoubles.InlinePushAttemptRecorder(),
            directory: directory,
            portalAccounts: portalAccounts
        );
    }

    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task Actor_name_is_filled_from_the_directory_for_staff_kinds()
    {
        var evt = await PublishWithActorAsync(NotificationKinds.TasksTaskOverdue, explicitName: null);

        Assert.Equal(ActorUserId, evt.ActorUserId);
        Assert.Equal("Ana Lopez", evt.ActorDisplayName);
    }

    [Fact]
    public async Task Actor_name_is_never_sent_for_portal_kinds()
    {
        var evt = await PublishWithActorAsync(NotificationKinds.TasksClientRequestCreated, explicitName: "Ana Lopez");

        Assert.Null(evt.ActorDisplayName);
    }

    [Fact]
    public async Task Actor_name_is_never_sent_to_a_portal_recipient_of_a_shared_kind()
    {
        var evt = await PublishWithActorAsync(
            NotificationKinds.CommunicationCallMissed,
            explicitName: null,
            recipientIsPortal: true
        );

        Assert.Null(evt.ActorDisplayName);
    }

    [Fact]
    public async Task Explicit_actor_name_is_kept()
    {
        var evt = await PublishWithActorAsync(NotificationKinds.TasksTaskOverdue, explicitName: "Ana (front desk)");

        Assert.Equal("Ana (front desk)", evt.ActorDisplayName);
    }

    private static async Task<InAppNotificationRequestedIntegrationEvent> PublishWithActorAsync(
        string kind,
        string? explicitName,
        bool recipientIsPortal = false
    )
    {
        var portalAccounts = new TestPortalAccounts();
        if (recipientIsPortal)
            portalAccounts.Rows.Add(CustomerPortalAccountProjection.Create(TenantId, Guid.NewGuid(), RecipientUserId));
        var directory = new InMemoryUserEmailDirectoryRepository();
        directory
            .Seed(TenantId, ActorUserId, "ana@example.com")
            .ApplyProfile(null, null, "Ana Lopez", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var publisher = new CapturingIntegrationEventPublisher();
        var dispatcher = BuildDispatcher(
            new RecordingNotificationLogRepository(),
            publisher,
            new NoOpUnitOfWork(),
            directory: directory,
            portalAccounts: portalAccounts
        );
        var sourceEventId = Guid.NewGuid();

        await dispatcher.RecordInAppAsync(
            TenantId,
            NotificationRecipient.ForUser(RecipientUserId),
            "Subject",
            NotificationCategory.Collaboration,
            kind,
            sourceEventId,
            "corr",
            recipientUserId: RecipientUserId,
            inApp: new InAppNotificationRequest(
                kind,
                RecipientUserId,
                sourceEventId,
                "test.v1",
                new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc),
                "Subject",
                ActorUserId: ActorUserId,
                ActorDisplayName: explicitName
            )
        );

        return Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(publisher.Published));
    }

    private sealed class RecordingNotificationLogRepository : INotificationLogRepository
    {
        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

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
            string? idempotencyToken,
            string? sourceRef = null
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
        public Task<int> RevokeForSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> RevokeAllActiveForUserExceptSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> DeactivateTokenInOtherTenantsAsync(
            string token,
            Guid exceptTenantId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

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
