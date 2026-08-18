using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

public sealed class ProcessWebhookHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private const string Phone = "+18095551234";
    private const string ProviderCode = "fake";

    private static SmsMessage AcceptedMessage(string providerMessageId)
    {
        var msg = SmsMessage
            .Create(
                Tenant,
                Customer,
                PhoneE164.Create(Phone).Value,
                SmsBody.Create("hi").Value,
                "idem-1",
                "corr-1",
                Guid.NewGuid(),
                ProviderCode,
                "docs",
                [],
                DateTime.UtcNow
            )
            .Value;
        msg.MarkAccepted(providerMessageId, DateTime.UtcNow);
        return msg;
    }

    // ─────────── Delivery receipts ───────────

    [Fact]
    public async Task Dlr_with_invalid_signature_is_rejected()
    {
        var provider = new FakeSmsProvider { SignatureValid = false };
        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            new FakeSmsMessageRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("sms.webhook.invalidSignature", result.Error.Code);
    }

    [Fact]
    public async Task Delivered_dlr_transitions_message_and_publishes_delivered_event()
    {
        var msg = AcceptedMessage("prov-1");
        var messages = new FakeSmsMessageRepository();
        messages.SeedForProviderMessageId(msg);
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("prov-1", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var processed = new FakeProcessedWebhookRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            processed,
            messages,
            new FakeUnitOfWork(),
            bus,
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsMessageStatus.Delivered, msg.Status);
        Assert.Single(processed.Added);
        Assert.NotNull(bus.LastOfType<SmsMessageDeliveredIntegrationEvent>());
    }

    [Fact]
    public async Task Duplicate_dlr_is_a_no_op()
    {
        var msg = AcceptedMessage("prov-1");
        var messages = new FakeSmsMessageRepository();
        messages.SeedForProviderMessageId(msg);
        var processed = new FakeProcessedWebhookRepository();
        processed.SeedExists(ProviderCode, "prov-1", "dlr");
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("prov-1", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var bus = new FakeMessageBus();

        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            processed,
            messages,
            new FakeUnitOfWork(),
            bus,
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsMessageStatus.Accepted, msg.Status); // unchanged
        Assert.Empty(bus.Published); // no duplicate event
    }

    [Fact]
    public async Task Dlr_for_unknown_message_is_ignored()
    {
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("ghost", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var bus = new FakeMessageBus();

        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            new FakeSmsMessageRepository(),
            new FakeUnitOfWork(),
            bus,
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
    }

    // ─────────── Inbound STOP/START ───────────

    [Fact]
    public async Task Inbound_stop_with_hints_opts_out()
    {
        var provider = new FakeSmsProvider
        {
            InboundMessage = new SmsInboundMessage(
                Phone,
                SmsInboundKeyword.Stop,
                "STOP",
                "inbound",
                "in-1",
                Tenant,
                Customer
            ),
        };
        var optOuts = new FakeSmsOptOutRepository();

        var result = await ProcessInboundHandler.Handle(
            new ProcessInboundCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            new FakeSmsMessageRepository(),
            optOuts,
            new FakeUnitOfWork(),
            NullLogger<ProcessInboundCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(optOuts.Added);
        Assert.True(stored.IsOptedOut);
        Assert.Equal(SmsOptOutStatus.OptedOut, stored.Status);
    }

    [Fact]
    public async Task Inbound_stop_resolves_tenant_from_latest_message_when_no_hints()
    {
        var last = AcceptedMessage("prov-9");
        var messages = new FakeSmsMessageRepository();
        messages.SeedLatestByPhone(last);
        var provider = new FakeSmsProvider
        {
            InboundMessage = new SmsInboundMessage(
                Phone,
                SmsInboundKeyword.Stop,
                "STOP",
                "inbound",
                "in-2",
                null,
                null
            ),
        };
        var optOuts = new FakeSmsOptOutRepository();

        var result = await ProcessInboundHandler.Handle(
            new ProcessInboundCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            messages,
            optOuts,
            new FakeUnitOfWork(),
            NullLogger<ProcessInboundCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(optOuts.Added);
        Assert.True(stored.IsOptedOut);
        Assert.Equal(Tenant, stored.TenantId);
    }

    [Fact]
    public async Task Inbound_that_cannot_be_resolved_is_a_recorded_no_op()
    {
        var provider = new FakeSmsProvider
        {
            InboundMessage = new SmsInboundMessage(
                Phone,
                SmsInboundKeyword.Stop,
                "STOP",
                "inbound",
                "in-3",
                null,
                null
            ),
        };
        var optOuts = new FakeSmsOptOutRepository();
        var processed = new FakeProcessedWebhookRepository();

        var result = await ProcessInboundHandler.Handle(
            new ProcessInboundCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            processed,
            new FakeSmsMessageRepository(), // GetLatestByPhone → null
            optOuts,
            new FakeUnitOfWork(),
            NullLogger<ProcessInboundCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(optOuts.Added); // no invented (tenant, customer)
        Assert.Single(processed.Added); // recorded for auditability
    }
}
