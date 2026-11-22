using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Mensajes sin leer: una fila por conversación que abre el chat.</summary>
public sealed class ChatMessagesMissedConsumerTests
{
    [Fact]
    public async Task Creates_one_grouped_row_per_conversation()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = new ChatMessagesMissedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ConversationKind = "Direct",
            RecipientUserId = Guid.NewGuid(),
            UnreadCount = 3,
            OldestUnreadAtUtc = DateTime.UtcNow.AddMinutes(-6),
            LatestMessageId = Guid.NewGuid(),
            LatestSenderId = Guid.NewGuid(),
        };

        await ChatMessagesMissedConsumer.Handle(evt, harness.Dispatcher, new NoOpCorrelationContext(), default);

        var row = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(evt.TenantId, row.TenantId);
        Assert.Equal(evt.RecipientUserId, row.RecipientUserId);
        Assert.Equal(NotificationKinds.CommunicationChatMessagesMissed, row.Kind);
        Assert.Equal("conversation", row.ResourceType);
        Assert.Equal(evt.ConversationId.ToString(), row.ResourceId);
        Assert.Equal($"chat:{evt.ConversationId:N}", row.GroupKey);
        Assert.Equal("3", row.Params["count"]);
        Assert.Equal(evt.LatestSenderId, row.ActorUserId);
    }

    [Fact]
    public async Task Without_recipient_nothing_is_written()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = new ChatMessagesMissedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ConversationKind = "Group",
            RecipientUserId = Guid.Empty,
            UnreadCount = 1,
            OldestUnreadAtUtc = DateTime.UtcNow,
            LatestMessageId = Guid.NewGuid(),
            LatestSenderId = Guid.NewGuid(),
        };

        await ChatMessagesMissedConsumer.Handle(evt, harness.Dispatcher, new NoOpCorrelationContext(), default);

        Assert.Empty(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
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
