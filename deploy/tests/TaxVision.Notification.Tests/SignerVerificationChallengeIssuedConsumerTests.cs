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

/// <summary>Reto de verificación del firmante: SMS y correo; el push "por app" no existe (D-N45).</summary>
public sealed class SignerVerificationChallengeIssuedConsumerTests
{
    // El firmante externo no tiene dispositivos: la rama AppPush no podía entregar nada.
    [Fact]
    public async Task App_push_is_not_a_supported_method()
    {
        var evt = BuildEvent();
        var logs = new RecordingNotificationLogRepository();

        await SignerVerificationChallengeIssuedConsumer.Handle(
            evt,
            NoOpEmailDispatchGateway.Instance,
            NoOpScribeRenderClient.Instance,
            NoOpSmsSender.Instance,
            logs,
            new NoOpUnitOfWork(),
            new NoOpCorrelationContext(),
            TestPlatform.Settings,
            NullLogger<SignerVerificationChallengeIssuedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.DoesNotContain(logs.Logs, log => log.Status == NotificationStatus.Sent);
    }

    /// <summary>P14: un código de minutos se dice relativo; "Vence a las 14:10 UTC" obligaba a convertir.</summary>
    [Theory]
    [InlineData("Es", "Vence en 10 minutos.")]
    [InlineData("En", "Expires in 10 minutes.")]
    public async Task Sms_code_says_how_many_minutes_it_lasts(string language, string expected)
    {
        var sms = new RecordingSmsSender();
        var evt = BuildEvent() with
        {
            Method = "SmsOtp",
            DeliveryAddress = "+15550100",
            SignerLanguage = language,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
        };

        await SignerVerificationChallengeIssuedConsumer.Handle(
            evt,
            NoOpEmailDispatchGateway.Instance,
            NoOpScribeRenderClient.Instance,
            sms,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork(),
            new NoOpCorrelationContext(),
            TestPlatform.Settings,
            NullLogger<SignerVerificationChallengeIssuedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var text = Assert.Single(sms.Texts);
        Assert.EndsWith(expected, text);
        Assert.DoesNotContain("UTC", text);
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
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        public List<string> Texts { get; } = [];

        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken,
            string? sourceRef = null
        )
        {
            Texts.Add(text);
            return Task.FromResult(Result.Success());
        }
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

    private sealed class NoOpEmailDispatchGateway : IEmailDispatchGateway
    {
        public static readonly NoOpEmailDispatchGateway Instance = new();

        // Nunca se invoca en esta suite — solo tiene que compilar.
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
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ct);

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public static readonly NoOpScribeRenderClient Instance = new();

        // Nunca se invoca en esta suite — solo tiene que compilar.
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
