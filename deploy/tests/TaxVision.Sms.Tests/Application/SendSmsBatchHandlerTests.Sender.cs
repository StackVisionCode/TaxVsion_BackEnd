using BuildingBlocks.Messaging.SmsIntegrationEvents;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

// Quién envió el SMS (Work Profile): se guarda y se avisa solo cuando lo envía una persona del staff.
public sealed partial class SendSmsBatchHandlerTests
{
    [Fact]
    public async Task Single_message_sent_by_staff_stores_the_sender_and_publishes_sent()
    {
        var h = new Harness();
        var sender = Guid.NewGuid();

        var result = await h.Run(BatchFrom(sender, canViewAll: true, Item()));

        Assert.True(result.IsSuccess);
        var message = Assert.Single(h.Messages.Added);
        Assert.Equal(sender, message.SentByUserId);
        var sent = Assert.Single(h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>());
        Assert.Equal(message.Id, sent.MessageId);
        Assert.Equal(Customer, sent.CustomerId);
        Assert.Equal(sender, sent.SentByUserId);
        Assert.Equal(Tenant, sent.TenantId);
    }

    [Fact]
    public async Task Batch_sent_by_staff_stores_the_sender_on_every_message()
    {
        var h = new Harness();
        var sender = Guid.NewGuid();

        await h.Run(
            BatchFrom(
                sender,
                canViewAll: true,
                Item(idempotencyKey: "idem-1"),
                Item(to: "+18095551235", idempotencyKey: "idem-2")
            )
        );

        Assert.Equal(2, h.Messages.Added.Count);
        Assert.All(h.Messages.Added, m => Assert.Equal(sender, m.SentByUserId));
        Assert.Equal(2, h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Message_without_a_staff_sender_has_no_sender_and_does_not_publish_sent()
    {
        var h = new Harness();

        await h.Run(Batch(Item()));

        Assert.Null(Assert.Single(h.Messages.Added).SentByUserId);
        Assert.Single(h.Bus.Published.OfType<SmsMessageAcceptedIntegrationEvent>());
        Assert.Empty(h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>());
    }

    [Fact]
    public async Task Rejected_message_does_not_publish_sent()
    {
        var h = new Harness();
        h.Provider.SendImpl = _ => new SmsSendResult(false, null, "providerRejected", "carrier down");

        await h.Run(BatchFrom(Guid.NewGuid(), canViewAll: true, Item()));

        Assert.Empty(h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>());
    }

    [Fact]
    public async Task Retried_batch_does_not_publish_sent_twice()
    {
        var h = new Harness();
        var sender = Guid.NewGuid();

        await h.Run(BatchFrom(sender, canViewAll: true, Item()));
        await h.Run(BatchFrom(sender, canViewAll: true, Item()));

        Assert.Equal(1, h.Provider.SendAsyncCallCount);
        Assert.Single(h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>());
    }

    [Fact]
    public async Task Replayed_send_result_does_not_publish_sent_twice()
    {
        var h = new Harness();
        await h.Run(BatchFrom(Guid.NewGuid(), canViewAll: true, Item()));
        var message = Assert.Single(h.Messages.Added);

        await RecordSmsSendResultHandler.Handle(
            new RecordSmsSendResultCommand(Tenant, message.Id, SmsSendOutcome.Accepted, "fake", "pm-1", null, null),
            h.Messages,
            h.OptOuts,
            new FakePendingDeliveryReceiptRepository(),
            new FakeProcessedWebhookRepository(),
            h.UnitOfWork,
            h.Bus,
            CancellationToken.None
        );

        Assert.Equal(SmsMessageStatus.Accepted, message.Status);
        Assert.Single(h.Bus.Published.OfType<SmsMessageSentIntegrationEvent>());
    }
}
