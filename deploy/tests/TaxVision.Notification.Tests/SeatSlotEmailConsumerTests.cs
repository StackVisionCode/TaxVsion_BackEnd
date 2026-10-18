using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

public sealed class SeatSlotEmailConsumerTests
{
    [Fact]
    public async Task Gated_email_is_disabled_by_default()
    {
        var gateway = new RecordingGateway();

        await HandleGatedAsync(Event(), gateway, new Logs(), emitNotifications: false);

        Assert.Empty(gateway.Requests);
    }

    [Fact]
    public async Task Gated_email_is_suppressed_while_an_unresolved_notification_is_younger_than_24_hours()
    {
        var evt = Event();
        var logs = new Logs();
        var gateway = new RecordingGateway();

        await HandleGatedAsync(evt, gateway, logs, emitNotifications: true);
        await HandleGatedAsync(evt with { EventId = Guid.NewGuid() }, gateway, logs, emitNotifications: true);

        Assert.Single(gateway.Requests);
        Assert.Single(logs.Values);
    }

    [Fact]
    public async Task Unlocked_event_resolves_the_gate_and_sends_the_informative_email()
    {
        var gated = Event();
        var logs = new Logs();
        var gateway = new RecordingGateway();
        await HandleGatedAsync(gated, gateway, logs, emitNotifications: true);

        await UserSlotUnlockedEmailConsumer.Handle(
            new UserSlotUnlockedIntegrationEvent
            {
                TenantId = gated.TenantId,
                UserId = gated.UserId,
                UserFullName = gated.UserFullName,
                UserEmail = gated.UserEmail,
                PrimaryAdminEmail = gated.PrimaryAdminEmail,
                TenantName = gated.TenantName,
                TenantSubdomain = gated.TenantSubdomain,
            },
            gateway,
            new Scribe(),
            logs,
            new UnitOfWork(),
            new OptionsMonitor(true),
            TestPlatform.Settings,
            new Correlation(),
            new FixedTimeProvider(),
            NullLogger<UserSlotUnlockedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(logs.Values.Single().IsResolved);
        Assert.Equal(2, gateway.Requests.Count);
        Assert.Equal("seats.user_unlocked.v1", gateway.Requests[1].TemplateKey);
    }

    private static UserSlotGatedIntegrationEvent Event() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            SlotIndex = 3,
            EffectiveCap = 2,
            UserFullName = "Ana Staff",
            UserEmail = "ana@acme.test",
            PrimaryAdminEmail = "owner@acme.test",
            TenantName = "Acme Tax",
            TenantSubdomain = "acme",
        };

    private static Task HandleGatedAsync(
        UserSlotGatedIntegrationEvent evt,
        RecordingGateway gateway,
        Logs logs,
        bool emitNotifications
    ) =>
        UserSlotGatedEmailConsumer.Handle(
            evt,
            gateway,
            new Scribe(),
            logs,
            new OptionsMonitor(emitNotifications),
            TestPlatform.Settings,
            new Correlation(),
            new FixedTimeProvider(),
            NullLogger<UserSlotGatedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class Logs : ISeatGateNotificationLogRepository
    {
        public List<SeatGateNotificationLog> Values { get; } = [];

        public Task<SeatGateNotificationLog?> GetOpenAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(Values.LastOrDefault(x => x.TenantId == tenantId && x.UserId == userId && !x.IsResolved));

        public Task AddAsync(SeatGateNotificationLog log, CancellationToken ct = default)
        {
            Values.Add(log);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Requests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                )
            );
        }
    }

    private sealed class Scribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new ScribeRenderedEmail(eventKey, "<p>Body</p>", "Body")));
    }

    private sealed class OptionsMonitor(bool emitNotifications) : IOptionsMonitor<SlotGatingOptions>
    {
        public SlotGatingOptions CurrentValue { get; } = new() { EmitNotifications = emitNotifications };

        public SlotGatingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SlotGatingOptions, string?> listener) => null;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Disposable();
        }

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
