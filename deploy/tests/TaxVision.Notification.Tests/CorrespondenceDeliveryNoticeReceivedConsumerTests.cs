using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

// Campana para quien envió un correo desde el CRM que no se entregó (R2).
public sealed class CorrespondenceDeliveryNoticeReceivedConsumerTests
{
    private static CorrespondenceDeliveryNoticeReceivedIntegrationEvent Notice(
        Guid sender,
        string outcome = "NotDelivered",
        Guid? customerId = null
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr",
            DeliveryNoticeId = Guid.NewGuid(),
            EmailThreadId = Guid.NewGuid(),
            DraftId = Guid.NewGuid(),
            CustomerId = customerId,
            MailboxAccountId = Guid.NewGuid(),
            SentByUserId = sender,
            Recipient = "maria@x.com",
            Outcome = outcome,
            Category = "InvalidAddress",
        };

    private static Task HandleAsync(
        NotificationDispatcherHarness harness,
        CorrespondenceDeliveryNoticeReceivedIntegrationEvent evt
    ) =>
        CorrespondenceDeliveryNoticeReceivedConsumer.Handle(
            evt,
            harness.Dispatcher,
            new TestCorrelationContext(),
            CancellationToken.None
        );

    [Fact]
    public async Task Not_delivered_notifies_the_sender_with_a_link_to_the_conversation()
    {
        var harness = new NotificationDispatcherHarness();
        var sender = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = Notice(sender, customerId: customerId);

        await HandleAsync(harness, evt);

        var inApp = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(
            Assert.Single(harness.Publisher.Published)
        );
        Assert.Equal(sender, inApp.RecipientUserId);
        Assert.Equal(NotificationKinds.CorrespondenceDeliveryFailed, inApp.Kind);
        Assert.Equal("Email to maria@x.com couldn't be delivered", inApp.FallbackTitle);
        Assert.Equal("email_thread", inApp.ResourceType);
        Assert.Equal(evt.EmailThreadId.ToString(), inApp.ResourceId);
        Assert.Equal(evt.MailboxAccountId.ToString(), inApp.ResourceContext["accountId"]);
        Assert.Equal(customerId.ToString(), inApp.ResourceContext["customerId"]);
        Assert.Equal($"email_thread:{evt.EmailThreadId}", inApp.GroupKey);
    }

    [Fact]
    public async Task A_not_client_recipient_has_no_customer_in_the_link()
    {
        var harness = new NotificationDispatcherHarness();

        await HandleAsync(harness, Notice(Guid.NewGuid()));

        var inApp = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(
            Assert.Single(harness.Publisher.Published)
        );
        Assert.False(inApp.ResourceContext.ContainsKey("customerId"));
    }

    [Fact]
    public async Task Delayed_or_an_unknown_sender_does_not_notify()
    {
        var harness = new NotificationDispatcherHarness();

        await HandleAsync(harness, Notice(Guid.NewGuid(), outcome: "Delayed"));
        await HandleAsync(harness, Notice(Guid.Empty));

        Assert.Empty(harness.Publisher.Published);
    }

    private sealed class TestCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
