using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Postmaster;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>Estado real de entrega en el path NotificationLog: Delivered y Bounced con sus transiciones.</summary>
public sealed class PostmasterDeliveryStatusConsumersTests
{
    private static (NotificationLog Log, Guid AttemptId) SentLog()
    {
        var log = NotificationLog
            .Create(Guid.NewGuid(), NotificationChannel.Email, "to@test.com", "Subject", "auth.welcome", null, null)
            .Value;
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        log.UpdateAttemptStatus(attempt.Id, NotificationDispatchAttemptStatus.Sent);
        log.MarkSent();
        return (log, attempt.Id);
    }

    private static Task DeliverAsync(NotificationLog log, Guid attemptId, CountingUnitOfWork uow) =>
        PostmasterEmailDeliveredConsumer.Handle(
            new PostmasterEmailDeliveredIntegrationEvent
            {
                TenantId = log.TenantId,
                NotificationLogId = log.Id,
                DispatchAttemptId = attemptId,
                SentMessageId = Guid.NewGuid(),
                EventAtUtc = DateTime.UtcNow,
            },
            new SingleLogRepository(log),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliveredIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static Task BounceAsync(NotificationLog log, Guid attemptId) =>
        PostmasterEmailDeliveryBouncedConsumer.Handle(
            new PostmasterEmailDeliveryBouncedIntegrationEvent
            {
                TenantId = log.TenantId,
                NotificationLogId = log.Id,
                DispatchAttemptId = attemptId,
                SentMessageId = Guid.NewGuid(),
                BounceType = "Hard",
                Reason = "550 user unknown",
                EventAtUtc = DateTime.UtcNow,
            },
            new SingleLogRepository(log),
            new CountingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliveryBouncedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Delivered_moves_the_attempt_and_the_log_to_Delivered()
    {
        var (log, attemptId) = SentLog();
        var uow = new CountingUnitOfWork();

        await DeliverAsync(log, attemptId, uow);

        Assert.Equal(NotificationStatus.Delivered, log.Status);
        Assert.Equal(NotificationDispatchAttemptStatus.Delivered, log.Attempts.Single().Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Delivered_before_sent_throws_dependency_not_ready()
    {
        var log = NotificationLog
            .Create(Guid.NewGuid(), NotificationChannel.Email, "to@test.com", "Subject", "auth.welcome", null, null)
            .Value;
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);
        var uow = new CountingUnitOfWork();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => DeliverAsync(log, attempt.Id, uow));

        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Bounced_before_sent_throws_dependency_not_ready()
    {
        var log = NotificationLog
            .Create(Guid.NewGuid(), NotificationChannel.Email, "to@test.com", "Subject", "auth.welcome", null, null)
            .Value;
        var attempt = log.AddDispatchAttempt(NotificationChannel.Email);

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => BounceAsync(log, attempt.Id));

        Assert.NotEqual(NotificationStatus.Bounced, log.Status);
    }

    [Fact]
    public async Task Bounce_marks_the_log_Bounced_not_Failed()
    {
        var (log, attemptId) = SentLog();

        await BounceAsync(log, attemptId);

        Assert.Equal(NotificationStatus.Bounced, log.Status);
        Assert.Equal("Bounce (Hard): 550 user unknown", log.Error);
    }

    [Fact]
    public async Task A_late_bounce_after_Delivered_wins()
    {
        var (log, attemptId) = SentLog();
        await DeliverAsync(log, attemptId, new CountingUnitOfWork());

        await BounceAsync(log, attemptId);

        Assert.Equal(NotificationStatus.Bounced, log.Status);
        Assert.Equal(NotificationDispatchAttemptStatus.Bounced, log.Attempts.Single().Status);
    }

    [Fact]
    public async Task Callback_with_other_tenant_does_not_touch_the_log()
    {
        var (log, attemptId) = SentLog();
        var uow = new CountingUnitOfWork();

        await PostmasterEmailDeliveredConsumer.Handle(
            new PostmasterEmailDeliveredIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                NotificationLogId = log.Id,
                DispatchAttemptId = attemptId,
                SentMessageId = Guid.NewGuid(),
                EventAtUtc = DateTime.UtcNow,
            },
            new SingleLogRepository(log),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<PostmasterEmailDeliveredIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(NotificationDispatchAttemptStatus.Sent, log.Attempts.Single().Status);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public void MarkDelivered_does_not_revive_a_failed_log()
    {
        var log = NotificationLog
            .Create(Guid.NewGuid(), NotificationChannel.Email, "to@test.com", "Subject", "auth.welcome", null, null)
            .Value;
        log.MarkFailed("smtp down");

        log.MarkDelivered();

        Assert.Equal(NotificationStatus.Failed, log.Status);
    }

    private sealed class SingleLogRepository(NotificationLog log) : INotificationLogQueryRepository
    {
        // Igual que el repositorio real: filtra por id y tenant.
        public Task<NotificationLog?> FindWithAttemptsAsync(
            Guid tenantId,
            Guid notificationLogId,
            CancellationToken ct
        ) => Task.FromResult<NotificationLog?>(notificationLogId == log.Id && tenantId == log.TenantId ? log : null);

        public Task<bool> ExistsAsync(Guid notificationLogId, CancellationToken ct) =>
            Task.FromResult(notificationLogId == log.Id);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class NoOpCorrelationContext : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
