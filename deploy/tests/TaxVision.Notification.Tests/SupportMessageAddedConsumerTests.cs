using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Mensaje en un ticket: de la oficina avisa a los agentes; del agente, a quien abrió.</summary>
public sealed class SupportMessageAddedConsumerTests
{
    private static readonly Guid OfficeTenantId = Guid.NewGuid();
    private static readonly Guid PlatformTenantId = Guid.NewGuid();
    private static readonly Guid OpenerId = Guid.NewGuid();
    private static readonly Guid TicketId = Guid.NewGuid();

    [Fact]
    public async Task Customer_message_on_unassigned_ticket_notifies_every_listed_agent_in_the_agent_tenant()
    {
        var agents = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var harness = new NotificationDispatcherHarness();

        await Handle(
            harness,
            Message("Customer", OpenerId) with
            {
                SupportRecipientUserIds = agents,
            },
            new FakeResolver([])
        );

        var rows = InApp(harness);
        Assert.Equal(agents.OrderBy(id => id), rows.Select(r => r.RecipientUserId).OrderBy(id => id));
        Assert.All(rows, row => Assert.Equal(PlatformTenantId, row.TenantId));
        Assert.All(rows, row => Assert.Equal(NotificationKinds.CommunicationSupportMessageReceived, row.Kind));
        Assert.All(rows, row => Assert.Equal($"support:{TicketId:N}", row.GroupKey));
        Assert.All(rows, row => Assert.Equal(TicketId.ToString(), row.ResourceId));
    }

    [Fact]
    public async Task Customer_message_on_assigned_ticket_notifies_only_the_assigned_agent()
    {
        var assigned = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();

        await Handle(
            harness,
            Message("Customer", OpenerId) with
            {
                AssignedAgentId = assigned,
                SupportRecipientUserIds = [Guid.NewGuid()],
            },
            new FakeResolver([])
        );

        Assert.Equal(assigned, Assert.Single(InApp(harness)).RecipientUserId);
    }

    [Fact]
    public async Task Empty_recipient_list_falls_back_to_the_permission_audience()
    {
        var agent = Guid.NewGuid();
        var resolver = new FakeResolver([agent]);
        var harness = new NotificationDispatcherHarness();

        await Handle(harness, Message("Customer", OpenerId), resolver);

        Assert.Equal(new ByPermission(PlatformTenantId, CommunicationPermissions.SupportAgent), resolver.LastAudience);
        Assert.Equal(agent, Assert.Single(InApp(harness)).RecipientUserId);
    }

    [Fact]
    public async Task Agent_reply_notifies_the_opener_in_the_office_tenant()
    {
        var harness = new NotificationDispatcherHarness();

        await Handle(
            harness,
            Message("Agent", PlatformTenantId) with
            {
                AssignedAgentId = Guid.NewGuid(),
            },
            new FakeResolver([])
        );

        var row = Assert.Single(InApp(harness));
        Assert.Equal(OfficeTenantId, row.TenantId);
        Assert.Equal(OpenerId, row.RecipientUserId);
        Assert.Equal(NotificationKinds.CommunicationSupportReplyReceived, row.Kind);
    }

    private static Task Handle(
        NotificationDispatcherHarness harness,
        SupportMessageAddedIntegrationEvent evt,
        FakeResolver resolver
    ) => SupportMessageAddedConsumer.Handle(evt, harness.Dispatcher, resolver, new NoOpCorrelationContext(), default);

    private static SupportMessageAddedIntegrationEvent Message(string role, Guid sender) =>
        new()
        {
            TenantId = OfficeTenantId,
            TicketId = TicketId,
            MessageId = Guid.NewGuid(),
            SenderUserId = sender,
            SenderRole = role,
            SentAtUtc = DateTime.UtcNow,
            ConversationId = Guid.NewGuid(),
            AgentTenantId = PlatformTenantId,
            OpenedByUserId = OpenerId,
        };

    private static InAppNotificationRequestedIntegrationEvent[] InApp(NotificationDispatcherHarness harness) =>
        [.. harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>()];

    private sealed class FakeResolver(IReadOnlyList<Guid> recipients) : IRecipientResolver
    {
        public ByPermission? LastAudience { get; private set; }

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default)
        {
            LastAudience = audience;
            return Task.FromResult(recipients);
        }

        public Task<BuildingBlocks.Results.Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(BuildingBlocks.Results.Result.Success(recipients));
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
