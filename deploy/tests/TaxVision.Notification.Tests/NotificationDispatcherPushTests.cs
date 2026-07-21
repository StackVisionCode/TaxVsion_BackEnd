using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Fase 7 del plan de notificaciones dinámicas — cobertura nueva de
/// <see cref="NotificationDispatcher.SendPushAsync"/>, sin cobertura previa (confirmado antes
/// de escribir esta suite). Cubre el fan-out best-effort a múltiples dispositivos y la
/// revocación automática de un <see cref="PushDeviceToken"/> cuando el proveedor confirma
/// <see cref="PushErrorCodes.TokenInvalid"/>.
/// </summary>
public sealed class NotificationDispatcherPushTests
{
    [Fact]
    public async Task SendPushAsync_entrega_a_un_solo_dispositivo_activo_y_marca_Sent()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var deviceId = pushDeviceTokens.AddActiveDevice(tenantId: TenantId, userId: UserId, token: "token-1");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.True(result.IsSuccess);
        Assert.Single(pushSender.Sent);
        Assert.Equal("token-1", pushSender.Sent[0].Token);
        Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Sent, logs.Logs[0].Status);
        Assert.DoesNotContain(deviceId, pushDeviceTokens.RevokedIds);
    }

    [Fact]
    public async Task SendPushAsync_fanout_best_effort_un_dispositivo_entregado_ya_cuenta_como_Sent()
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error("Notification.PushFailed", "device offline"));
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-fails-first");
        pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-succeeds-second");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, pushSender.Sent.Count);
        Assert.Equal(NotificationStatus.Sent, logs.Logs[0].Status);
    }

    [Fact]
    public async Task SendPushAsync_revoca_el_device_cuando_el_proveedor_confirma_TokenInvalid()
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error(PushErrorCodes.TokenInvalid, "FCM token is no longer registered."));
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var deadDeviceId = pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-uninstalled");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.False(result.IsSuccess);
        Assert.Contains(deadDeviceId, pushDeviceTokens.RevokedIds);
    }

    [Fact]
    public async Task SendPushAsync_sin_dispositivos_activos_falla_con_NoPushDevices_y_no_intenta_revocar()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.False(result.IsSuccess);
        Assert.Equal("Notification.NoPushDevices", result.Error.Code);
        Assert.Empty(pushSender.Sent);
        Assert.Empty(pushDeviceTokens.RevokedIds);
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static NotificationDispatcher BuildDispatcher(
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logs,
        IUnitOfWork uow
    ) =>
        new(
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            AlwaysEnabledUserNotificationPreferenceRepository.Instance,
            uow,
            NullLogger<NotificationDispatcher>.Instance
        );

    // ------------------------------------------------------------------
    // Fakes — mismo estilo hand-rolled que BaselineSmokeTests, sin librería de mocking.
    // ------------------------------------------------------------------

    private sealed class RecordingPushSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];
        private Error? _nextFailure;

        public void FailNextWith(Error error) => _nextFailure = error;

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            if (_nextFailure is { } error)
            {
                _nextFailure = null;
                return Task.FromResult(Result.Failure(error));
            }
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakePushDeviceTokenRepository : IPushDeviceTokenRepository
    {
        private readonly Dictionary<Guid, PushDeviceToken> _devices = [];
        public List<Guid> RevokedIds { get; } = [];

        public Guid AddActiveDevice(Guid tenantId, Guid userId, string token)
        {
            var device = PushDeviceToken.Register(tenantId, userId, PushPlatform.Fcm, token, deviceId: null).Value;
            _devices[device.Id] = device;
            return device.Id;
        }

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default)
        {
            _devices[token.Id] = token;
            return Task.CompletedTask;
        }

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult(_devices.Values.FirstOrDefault(d => d.TenantId == tenantId && d.Token == token));

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_devices.TryGetValue(id, out var d) && d.TenantId == tenantId ? d : null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>(
                _devices.Values.Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive).ToList()
            );

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        {
            if (_devices.TryGetValue(id, out var d) && d.TenantId == tenantId)
            {
                d.Revoke();
                RevokedIds.Add(id);
            }
            return Task.CompletedTask;
        }
    }

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

    private sealed class NoOpSmsSender : ISmsSender
    {
        public static readonly NoOpSmsSender Instance = new();

        public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class AlwaysEnabledUserNotificationPreferenceRepository : IUserNotificationPreferenceRepository
    {
        public static readonly AlwaysEnabledUserNotificationPreferenceRepository Instance = new();

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
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>(Array.Empty<UserNotificationPreference>());

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
