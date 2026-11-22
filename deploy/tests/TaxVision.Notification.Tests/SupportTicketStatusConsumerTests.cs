using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Reclamado, resuelto y cerrado avisan solo a quien abrió el ticket, en su oficina.</summary>
public sealed class SupportTicketStatusConsumerTests
{
    private static readonly Guid OfficeTenantId = Guid.NewGuid();
    private static readonly Guid PlatformTenantId = Guid.NewGuid();
    private static readonly Guid OpenerId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public async Task Claimed_notifies_the_opener_in_the_office_tenant()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = new SupportClaimedIntegrationEvent
        {
            TenantId = OfficeTenantId,
            TicketId = Guid.NewGuid(),
            AssignedAgentId = AgentId,
            ClaimedAtUtc = DateTime.UtcNow,
            OpenedByUserId = OpenerId,
            AgentTenantId = PlatformTenantId,
            ConversationId = Guid.NewGuid(),
        };

        await SupportTicketStatusConsumer.Handle(
            evt,
            harness.Dispatcher,
            new NoOpCorrelationContext(),
            Logger<SupportClaimedIntegrationEvent>(),
            default
        );

        var row = Assert.Single(InApp(harness));
        Assert.Equal(OfficeTenantId, row.TenantId);
        Assert.Equal(OpenerId, row.RecipientUserId);
        Assert.Equal(NotificationKinds.CommunicationSupportClaimed, row.Kind);
        Assert.Equal(evt.TicketId.ToString(), row.ResourceId);
        Assert.Equal("support_ticket", row.ResourceType);
    }

    [Fact]
    public async Task Resolved_by_an_agent_notifies_the_opener()
    {
        var harness = new NotificationDispatcherHarness();

        await SupportTicketStatusConsumer.Handle(
            Resolved(byUserId: AgentId),
            harness.Dispatcher,
            new NoOpCorrelationContext(),
            Logger<SupportResolvedIntegrationEvent>(),
            default
        );

        var row = Assert.Single(InApp(harness));
        Assert.Equal(NotificationKinds.CommunicationSupportResolved, row.Kind);
        Assert.Equal(OpenerId, row.RecipientUserId);
    }

    [Fact]
    public async Task Resolved_by_the_opener_does_not_notify()
    {
        var harness = new NotificationDispatcherHarness();

        await SupportTicketStatusConsumer.Handle(
            Resolved(byUserId: OpenerId),
            harness.Dispatcher,
            new NoOpCorrelationContext(),
            Logger<SupportResolvedIntegrationEvent>(),
            default
        );

        Assert.Empty(InApp(harness));
    }

    [Fact]
    public async Task Closed_notifies_the_opener()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = new SupportClosedIntegrationEvent
        {
            TenantId = OfficeTenantId,
            TicketId = Guid.NewGuid(),
            ClosedByUserId = AgentId,
            ClosedAtUtc = DateTime.UtcNow,
            OpenedByUserId = OpenerId,
            AgentTenantId = PlatformTenantId,
            ConversationId = Guid.NewGuid(),
        };

        await SupportTicketStatusConsumer.Handle(
            evt,
            harness.Dispatcher,
            new NoOpCorrelationContext(),
            Logger<SupportClosedIntegrationEvent>(),
            default
        );

        Assert.Equal(NotificationKinds.CommunicationSupportClosed, Assert.Single(InApp(harness)).Kind);
    }

    [Fact]
    public async Task Event_without_opener_is_ignored()
    {
        var harness = new NotificationDispatcherHarness();
        var logger = Logger<SupportClaimedIntegrationEvent>();
        var evt = new SupportClaimedIntegrationEvent
        {
            TenantId = OfficeTenantId,
            TicketId = Guid.NewGuid(),
            AssignedAgentId = AgentId,
            ClaimedAtUtc = DateTime.UtcNow,
        };

        await SupportTicketStatusConsumer.Handle(
            evt,
            harness.Dispatcher,
            new NoOpCorrelationContext(),
            logger,
            default
        );

        Assert.Empty(InApp(harness));
        Assert.Single(logger.Warnings);
    }

    private static SupportResolvedIntegrationEvent Resolved(Guid byUserId) =>
        new()
        {
            TenantId = OfficeTenantId,
            TicketId = Guid.NewGuid(),
            ResolvedByUserId = byUserId,
            ResolvedByAgent = byUserId == AgentId,
            ResolvedAtUtc = DateTime.UtcNow,
            OpenedByUserId = OpenerId,
            AgentTenantId = PlatformTenantId,
            ConversationId = Guid.NewGuid(),
        };

    private static ListLogger<T> Logger<T>() => new();

    private static InAppNotificationRequestedIntegrationEvent[] InApp(NotificationDispatcherHarness harness) =>
        [.. harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>()];

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
