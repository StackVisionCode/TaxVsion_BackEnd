using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Perdidas del mismo contacto se agrupan y la fila abre la conversación.</summary>
public sealed class CallMissedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CalleeId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    [Fact]
    public async Task Missed_calls_from_the_same_caller_share_a_group_key()
    {
        var harness = new NotificationDispatcherHarness();

        await Handle(harness, Missed());
        await Handle(harness, Missed());

        var keys = InApp(harness).Select(row => row.GroupKey).Distinct().ToArray();
        Assert.Equal([$"call-missed:{CallerId:N}"], keys);
    }

    [Fact]
    public async Task Resource_points_to_the_conversation_when_known()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = Missed() with { ConversationId = Guid.NewGuid(), Reason = "Cancelled" };

        await Handle(harness, evt);

        var row = Assert.Single(InApp(harness));
        Assert.Equal(evt.ConversationId.ToString(), row.ResourceId);
        Assert.Equal(evt.CallId.ToString(), row.ResourceContext["callId"]);
        Assert.Equal(evt.ConversationId.ToString(), row.ResourceContext["conversationId"]);
        Assert.Equal("Audio", row.Params["kind"]);
    }

    [Fact]
    public async Task Without_conversation_the_resource_id_is_null()
    {
        var harness = new NotificationDispatcherHarness();

        await Handle(harness, Missed());

        var row = Assert.Single(InApp(harness));
        Assert.Null(row.ResourceId);
        Assert.False(row.ResourceContext.ContainsKey("conversationId"));
    }

    private static Task Handle(NotificationDispatcherHarness harness, CallMissedIntegrationEvent evt) =>
        CallMissedConsumer.Handle(evt, harness.Dispatcher, new AllowCallee(), new NoOpCorrelationContext(), default);

    private static CallMissedIntegrationEvent Missed() =>
        new()
        {
            TenantId = TenantId,
            CallId = Guid.NewGuid(),
            CallerUserId = CallerId,
            CalleeUserId = CalleeId,
            Kind = "Audio",
            RingingAtUtc = DateTime.UtcNow.AddSeconds(-40),
            MissedAtUtc = DateTime.UtcNow,
        };

    private static InAppNotificationRequestedIntegrationEvent[] InApp(NotificationDispatcherHarness harness) =>
        [.. harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>()];

    private sealed class AllowCallee : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([CalleeId]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<Guid>>([CalleeId]));
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
