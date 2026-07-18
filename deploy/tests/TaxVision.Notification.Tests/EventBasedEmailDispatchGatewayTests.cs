using System.Collections.Concurrent;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Postmaster;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Tests unitarios de <see cref="EventBasedEmailDispatchGateway"/> (Notifications Fase 4) y los 5
/// consumers de callback de Postmaster. Verifican que el request se publica con
/// <c>NotificationLogId</c> y <c>DispatchAttemptId</c>, y que cada callback transiciona el attempt
/// al estado esperado.
/// </summary>
public sealed class EventBasedEmailDispatchGatewayTests
{
    [Fact]
    public async Task QueueEmailAsync_publishes_event_and_persists_log_in_Pending_Queued_state()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var logRepo = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var gateway = new EventBasedEmailDispatchGateway(
            publisher,
            logRepo,
            uow,
            NullLogger<EventBasedEmailDispatchGateway>.Instance
        );

        var tenantId = Guid.NewGuid();
        var request = new EmailDispatchRequest(
            TenantId: tenantId,
            To: "customer@test.com",
            Subject: "Sign this",
            HtmlBody: "<p>Hi</p>",
            TextBody: "Hi",
            TemplateKey: "signature.signer_invited",
            RelatedEventId: Guid.NewGuid(),
            CorrelationId: "corr-1",
            Scope: EmailDispatchScope.Tenant
        );

        var result = await gateway.QueueEmailAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationDispatchAttemptStatus.Queued, result.Status);
        Assert.Single(logRepo.Logs);
        var log = logRepo.Logs[0];
        Assert.Equal(NotificationStatus.Pending, log.Status); // gateway NO marca sent — espera callback
        Assert.Single(log.Attempts);
        Assert.Equal(NotificationDispatchAttemptStatus.Queued, log.Attempts.First().Status);
        Assert.Single(publisher.Published);
        var evt = Assert.IsType<NotificationsEmailSendRequestedIntegrationEvent>(publisher.Published.First());
        Assert.Equal(log.Id, evt.NotificationLogId);
        Assert.Equal(log.Attempts.First().Id, evt.DispatchAttemptId);
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal("Tenant", evt.RequiredProviderScope);
        Assert.Equal("Tenant", evt.LogoScope);
        Assert.Equal("Transactional", evt.Stream);
        Assert.Equal(1, uow.SaveCount);
    }

    /// <summary>
    /// Hardening Fase 9 — prueba el segundo eslabón de la cadena: lo que un consumer pone en
    /// <c>EmailDispatchRequest.InlineAssets</c> (tomado de <c>ScribeRenderedEmail.InlineAssets</c>)
    /// tiene que llegar SIN transformación al evento publicado hacia Postmaster. Antes de esta fase
    /// el campo no existía en ninguno de los dos tipos.
    /// </summary>
    [Fact]
    public async Task QueueEmailAsync_propagates_InlineAssets_from_request_to_published_event()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var logRepo = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var gateway = new EventBasedEmailDispatchGateway(
            publisher,
            logRepo,
            uow,
            NullLogger<EventBasedEmailDispatchGateway>.Instance
        );

        var cloudStorageFileId = Guid.NewGuid();
        var inlineAssets = new List<EmailInlineAssetReference>
        {
            new("logo-header", cloudStorageFileId, "image/png", 8_192),
        };
        var request = new EmailDispatchRequest(
            TenantId: Guid.NewGuid(),
            To: "customer@test.com",
            Subject: "Welcome",
            HtmlBody: "<p>Hi <img src=\"cid:logo-header\"/></p>",
            TextBody: "Hi",
            TemplateKey: "auth.welcome",
            RelatedEventId: Guid.NewGuid(),
            CorrelationId: "corr-2",
            InlineAssets: inlineAssets
        );

        await gateway.QueueEmailAsync(request, CancellationToken.None);

        var evt = Assert.IsType<NotificationsEmailSendRequestedIntegrationEvent>(Assert.Single(publisher.Published));
        var published = Assert.Single(evt.InlineAssets!);
        Assert.Equal("logo-header", published.ContentId);
        Assert.Equal(cloudStorageFileId, published.CloudStorageFileId);
        Assert.Equal("image/png", published.ContentType);
        Assert.Equal(8_192, published.SizeBytes);
    }

    [Fact]
    public async Task SucceededCallback_transitions_attempt_to_Sent()
    {
        var log = CreateLogWithAttempt(out var attempt);
        var query = new StubQueryRepository(log);
        var uow = new NoOpUnitOfWork();
        var correlation = new NoOpCorrelationContext();

        var evt = new PostmasterEmailDeliverySucceededIntegrationEvent
        {
            TenantId = log.TenantId,
            NotificationLogId = log.Id,
            DispatchAttemptId = attempt.Id,
            SentMessageId = Guid.NewGuid(),
            ProviderMessageId = "smtp-msg-id-abc",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterEmailDeliverySucceededConsumer.Handle(
            evt,
            query,
            uow,
            correlation,
            NullLogger<PostmasterEmailDeliverySucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationDispatchAttemptStatus.Sent, attempt.Status);
        Assert.Equal("smtp-msg-id-abc", attempt.ProviderMessageId);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task FailedCallback_transitions_attempt_to_Failed_and_stores_reason()
    {
        var log = CreateLogWithAttempt(out var attempt);
        var query = new StubQueryRepository(log);
        var uow = new NoOpUnitOfWork();

        // Move attempt to Sent first, then fail (Sent → Failed is allowed).
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);

        var evt = new PostmasterEmailDeliveryFailedIntegrationEvent
        {
            TenantId = log.TenantId,
            NotificationLogId = log.Id,
            DispatchAttemptId = attempt.Id,
            SentMessageId = Guid.NewGuid(),
            Reason = "550 mailbox not found",
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterEmailDeliveryFailedConsumer.Handle(
            evt,
            query,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliveryFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationDispatchAttemptStatus.Failed, attempt.Status);
        Assert.Equal("550 mailbox not found", attempt.ErrorReason);
        Assert.Equal(NotificationStatus.Failed, log.Status);
    }

    [Fact]
    public async Task ProviderNotConfiguredCallback_transitions_attempt_and_marks_log_failed()
    {
        var log = CreateLogWithAttempt(out var attempt);
        var query = new StubQueryRepository(log);
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent
        {
            TenantId = log.TenantId,
            NotificationLogId = log.Id,
            DispatchAttemptId = attempt.Id,
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterEmailDeliveryProviderNotConfiguredConsumer.Handle(
            evt,
            query,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationDispatchAttemptStatus.ProviderNotConfigured, attempt.Status);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Contains("SMTP", attempt.ErrorReason ?? "");
    }

    [Fact]
    public async Task Callback_for_unknown_log_is_silently_dropped()
    {
        var query = new StubQueryRepository(null);
        var uow = new NoOpUnitOfWork();

        var evt = new PostmasterEmailDeliverySucceededIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            NotificationLogId = Guid.NewGuid(), // no existe
            DispatchAttemptId = Guid.NewGuid(),
            SentMessageId = Guid.NewGuid(),
            EventAtUtc = DateTime.UtcNow,
        };

        await PostmasterEmailDeliverySucceededConsumer.Handle(
            evt,
            query,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliverySucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, uow.SaveCount);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static NotificationLog CreateLogWithAttempt(out NotificationDispatchAttempt attempt)
    {
        var log = NotificationLog
            .Create(
                tenantId: Guid.NewGuid(),
                channel: NotificationChannel.Email,
                recipient: "test@customer.com",
                subject: "Test",
                templateKey: "test.key",
                relatedEventId: null,
                correlationId: null
            )
            .Value;
        attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        return log;
    }

    private sealed class StubQueryRepository(NotificationLog? log) : INotificationLogQueryRepository
    {
        public Task<NotificationLog?> FindWithAttemptsAsync(Guid notificationLogId, CancellationToken ct) =>
            Task.FromResult(log);
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

    private sealed class NoOpCorrelationContext : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>Fake mínimo del port <see cref="IIntegrationEventPublisher"/> — captura los mensajes.</summary>
    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public ConcurrentBag<object> Published { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}
