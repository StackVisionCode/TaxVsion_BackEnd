using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Fase 7 del plan de notificaciones dinámicas — cobertura nueva de la rama
/// AppPush/<c>SendAppPushAsync</c> de <see cref="SignerVerificationChallengeIssuedConsumer"/>,
/// sin cobertura previa (confirmado antes de escribir esta suite: solo las ramas de email
/// tenían tests). Incluye el mismo criterio de revocación de <see cref="PushDeviceToken"/> que
/// <c>NotificationDispatcherPushTests</c>.
/// </summary>
public sealed class SignerVerificationChallengeAppPushTests
{
    [Fact]
    public async Task AppPush_entrega_al_dispositivo_activo_del_firmante_y_marca_Sent()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var evt = BuildEvent();
        pushDeviceTokens.AddActiveDevice(evt.TenantId, evt.SignerId, "signer-device-token");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();

        await SignerVerificationChallengeIssuedConsumer.Handle(
            evt,
            NoOpEmailDispatchGateway.Instance,
            NoOpScribeRenderClient.Instance,
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<SignerVerificationChallengeIssuedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Single(pushSender.Sent);
        Assert.Equal("signer-device-token", pushSender.Sent[0].Token);
        Assert.Single(logs.Logs);
        Assert.Equal(NotificationChannel.Push, logs.Logs[0].Channel);
        Assert.Equal(NotificationStatus.Sent, logs.Logs[0].Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task AppPush_sin_dispositivos_registrados_marca_el_log_como_Failed_sin_lanzar()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var evt = BuildEvent();
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();

        await SignerVerificationChallengeIssuedConsumer.Handle(
            evt,
            NoOpEmailDispatchGateway.Instance,
            NoOpScribeRenderClient.Instance,
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<SignerVerificationChallengeIssuedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(pushSender.Sent);
        Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Failed, logs.Logs[0].Status);
    }

    [Fact]
    public async Task AppPush_revoca_el_device_cuando_el_proveedor_confirma_TokenInvalid()
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error(PushErrorCodes.TokenInvalid, "FCM token is no longer registered."));
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var evt = BuildEvent();
        var deadDeviceId = pushDeviceTokens.AddActiveDevice(evt.TenantId, evt.SignerId, "uninstalled-token");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();

        await SignerVerificationChallengeIssuedConsumer.Handle(
            evt,
            NoOpEmailDispatchGateway.Instance,
            NoOpScribeRenderClient.Instance,
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<SignerVerificationChallengeIssuedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Contains(deadDeviceId, pushDeviceTokens.RevokedIds);
        Assert.Equal(NotificationStatus.Failed, logs.Logs[0].Status);
    }

    private static SignerVerificationChallengeIssuedIntegrationEvent BuildEvent() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            Method = "AppPush",
            DeliveryAddress = string.Empty,
            PlaintextAnswer = "000000",
            SignerFullName = "Firmante de Prueba",
            SignerLanguage = "Es",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
        };

    // ------------------------------------------------------------------
    // Fakes — mismo estilo hand-rolled que el resto de la suite de Notification.
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

    private sealed class NoOpEmailDispatchGateway : IEmailDispatchGateway
    {
        public static readonly NoOpEmailDispatchGateway Instance = new();

        // Nunca se invoca en esta suite (solo cubre la rama AppPush) — solo tiene que compilar.
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Sent,
                    null,
                    null
                )
            );
    }

    private sealed class NoOpScribeRenderClient : IScribeRenderClient
    {
        public static readonly NoOpScribeRenderClient Instance = new();

        // Nunca se invoca en esta suite (solo cubre la rama AppPush) — solo tiene que compilar.
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) =>
            Task.FromResult(Result.Failure<ScribeRenderedEmail>(new Error("Test.Unused", "Not invoked in this test.")));
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpDisposable();
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
