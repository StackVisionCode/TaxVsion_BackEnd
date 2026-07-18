using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Baseline smoke suite exigida por Notifications Fase 0 (Notifications_Service_Responsibility_Cleanup_Plan §36).
/// Se actualiza en Fase 3: los consumers de email ahora reciben <see cref="IEmailDispatchGateway"/> en vez de
/// <see cref="NotificationDispatcher"/>. El comportamiento observable a nivel de aggregate es idéntico
/// (NotificationLog + attempt + status Sent) — el gateway InProcess reproduce el flujo del dispatcher.
///
/// <para>
/// Alcance: fake-based unit tests que invocan directamente el método Handle de cada consumer
/// estático. Ver Baseline_Snapshot.md §9 para la limitación honesta.
/// </para>
/// </summary>
public sealed class BaselineSmokeTests
{
    [Fact]
    public async Task PasswordReset_Consumer_Invokes_Gateway_And_Records_Sent()
    {
        var emailSender = new RecordingEmailSender();
        var logRepo = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var gateway = new InProcessEmailDispatchGateway(
            emailSender,
            logRepo,
            uow,
            NullLogger<InProcessEmailDispatchGateway>.Instance
        );
        var portal = Options.Create(new PortalOptions { BaseUrl = "https://app.test", ProductName = "TaxVision" });
        var correlation = new NoOpCorrelationContext();
        var scribeClient = new FakeScribeRenderClient();

        var evt = new PasswordResetRequestedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Email = "user@test.com",
            RawToken = "raw-token-123",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        };

        await PasswordResetRequestedConsumer.Handle(
            evt,
            gateway,
            scribeClient,
            portal,
            correlation,
            CancellationToken.None
        );

        Assert.Single(emailSender.Sent);
        Assert.Equal("user@test.com", emailSender.Sent[0].To);
        Assert.Single(logRepo.Logs);
        var log = logRepo.Logs[0];
        Assert.Equal(NotificationChannel.Email, log.Channel);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Single(log.Attempts);
        Assert.Equal(NotificationDispatchAttemptStatus.Sent, log.Attempts.First().Status);
        Assert.Equal(evt.TenantId, log.TenantId);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task SignerInvited_Consumer_Invokes_Gateway_And_Records_Sent()
    {
        var emailSender = new RecordingEmailSender();
        var logRepo = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var gateway = new InProcessEmailDispatchGateway(
            emailSender,
            logRepo,
            uow,
            NullLogger<InProcessEmailDispatchGateway>.Instance
        );
        var correlation = new NoOpCorrelationContext();
        var scribeClient = new FakeScribeRenderClient();

        var evt = new SignerInvitedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            Email = "signer@customer.com",
            FullName = "Ada Lovelace",
            Order = 1,
            Language = "En",
            PublicUrl = "https://app.test/sign/abc",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            RevocationEpoch = 0,
            RequiresConsent = false,
            RequiresSequentialSigning = false,
        };

        await SignerInvitedConsumer.Handle(
            evt,
            gateway,
            scribeClient,
            correlation,
            NullLogger<SignerInvitedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Single(emailSender.Sent);
        Assert.Equal("signer@customer.com", emailSender.Sent[0].To);
        Assert.Single(logRepo.Logs);
        var log = logRepo.Logs[0];
        Assert.Equal(NotificationChannel.Email, log.Channel);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Single(log.Attempts);
        Assert.Equal(NotificationDispatchAttemptStatus.Sent, log.Attempts.First().Status);
        Assert.Equal(evt.TenantId, log.TenantId);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task MeetingRecordingReady_Consumer_Records_InApp_NotificationLog()
    {
        var emailSender = new RecordingEmailSender();
        var logRepo = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var correlation = new NoOpCorrelationContext();
        var dispatcher = new NotificationDispatcher(
            NoOpSmsSender.Instance,
            NoOpPushSender.Instance,
            EmptyPushDeviceTokenRepository.Instance,
            logRepo,
            uow,
            NullLogger<NotificationDispatcher>.Instance
        );

        var meetingId = Guid.NewGuid();
        var evt = new MeetingRecordingReadyIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            MeetingId = meetingId,
            RecordingFileId = Guid.NewGuid(),
            DurationSeconds = 1800,
            ParticipantCount = 3,
            ReadyAtUtc = DateTime.UtcNow,
        };

        await MeetingRecordingReadyConsumer.Handle(evt, dispatcher, correlation, CancellationToken.None);

        Assert.Empty(emailSender.Sent); // in-app no envía email
        Assert.Single(logRepo.Logs);
        var log = logRepo.Logs[0];
        Assert.Equal(NotificationChannel.InApp, log.Channel);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal($"meeting:{meetingId:N}", log.Recipient);
        Assert.Equal(evt.TenantId, log.TenantId);
        Assert.Equal(1, uow.SaveCount);
    }

    // ------------------------------------------------------------------
    // Fakes deterministas
    // ------------------------------------------------------------------

    private sealed class FakeScribeRenderClient : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new ScribeRenderedEmail("Test subject", "<p>Test body</p>", "Test body")));
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class RecordingNotificationLogRepository : INotificationLogRepository
    {
        public List<NotificationLog> Logs { get; } = new();

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

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public static readonly NoOpSmsSender Instance = new();

        public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
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
        ) => Task.FromResult<IReadOnlyList<PushDeviceToken>>(Array.Empty<PushDeviceToken>());
    }
}
