using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Domain.Webhooks;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

public sealed class ProcessWebhookHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private const string Phone = "+18095551234";
    private const string ProviderCode = "fake";

    private readonly FakePendingDeliveryReceiptRepository _pending = new();

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
            new FakePendingDeliveryReceiptRepository(),
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
            _pending,
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

    // SMS-04: sin cambio de estado no hay nada que avisar.
    [Fact]
    public async Task Second_dlr_type_after_delivered_does_not_republish()
    {
        var msg = AcceptedMessage("prov-1");
        msg.MarkDelivered(DateTime.UtcNow);
        var messages = new FakeSmsMessageRepository();
        messages.SeedForProviderMessageId(msg);
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("prov-1", "dlr-final", SmsCanonicalStatus.Delivered, null, null),
        };
        var processed = new FakeProcessedWebhookRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            processed,
            _pending,
            messages,
            new FakeUnitOfWork(),
            bus,
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(processed.Added);
        Assert.Empty(bus.Published);
    }

    // Guardar antes de publicar: un guardado fallido no deja avisos sueltos.
    [Fact]
    public async Task Dlr_save_failure_publishes_nothing()
    {
        var msg = AcceptedMessage("prov-1");
        var messages = new FakeSmsMessageRepository();
        messages.SeedForProviderMessageId(msg);
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("prov-1", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessDeliveryReceiptHandler.Handle(
                new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
                new FakeSmsAdapterFactory(provider),
                new FakeSmsWebhookSecrets(),
                new FakeProcessedWebhookRepository(),
                new FakePendingDeliveryReceiptRepository(),
                messages,
                new FakeUnitOfWork { FailNextSave = true },
                bus,
                NullLogger<ProcessDeliveryReceiptCommand>.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(bus.Published);
    }

    // SMS-04: el DLR puede llegar antes del commit del envío. No se pierde: se aplica al aceptar.
    [Fact]
    public async Task Dlr_for_unknown_message_is_kept_and_applied_on_accept()
    {
        var messages = new FakeSmsMessageRepository();
        var processed = new FakeProcessedWebhookRepository();
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("prov-early", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var bus = new FakeMessageBus();

        var received = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
            new FakeSmsAdapterFactory(provider),
            new FakeSmsWebhookSecrets(),
            processed,
            _pending,
            messages,
            new FakeUnitOfWork(),
            bus,
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );
        Assert.True(received.IsSuccess);
        Assert.Single(_pending.Receipts);

        var msg = SmsMessage
            .Create(
                Tenant,
                Customer,
                PhoneE164.Create(Phone).Value,
                SmsBody.Create("hi").Value,
                "idem-early",
                "corr-1",
                Guid.NewGuid(),
                ProviderCode,
                "docs",
                [],
                DateTime.UtcNow
            )
            .Value;
        await messages.AddAsync(msg);

        await RecordSmsSendResultHandler.Handle(
            new RecordSmsSendResultCommand(
                Tenant,
                msg.Id,
                SmsSendOutcome.Accepted,
                ProviderCode,
                "prov-early",
                null,
                null
            ),
            messages,
            new FakeSmsOptOutRepository(),
            _pending,
            processed,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.Equal(SmsMessageStatus.Delivered, msg.Status);
        Assert.Empty(_pending.Receipts);
        Assert.Single(processed.Added);
        Assert.NotNull(bus.LastOfType<SmsMessageAcceptedIntegrationEvent>());
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
            _pending,
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
    public async Task Repeated_dlr_for_unknown_message_is_kept_once()
    {
        var provider = new FakeSmsProvider
        {
            DeliveryUpdate = new SmsDeliveryUpdate("ghost", "dlr", SmsCanonicalStatus.Delivered, null, null),
        };
        var bus = new FakeMessageBus();

        for (var delivery = 0; delivery < 2; delivery++)
        {
            var result = await ProcessDeliveryReceiptHandler.Handle(
                new ProcessDeliveryReceiptCommand(ProviderCode, "{}", "sig"),
                new FakeSmsAdapterFactory(provider),
                new FakeSmsWebhookSecrets(),
                new FakeProcessedWebhookRepository(),
                _pending,
                new FakeSmsMessageRepository(),
                new FakeUnitOfWork(),
                bus,
                NullLogger<ProcessDeliveryReceiptCommand>.Instance,
                CancellationToken.None
            );
            Assert.True(result.IsSuccess);
        }

        Assert.Single(_pending.Receipts);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Purge_removes_only_the_receipts_past_the_retention()
    {
        var nowUtc = DateTime.UtcNow;
        await _pending.AddAsync(Receipt("old", nowUtc.AddDays(-8)));
        await _pending.AddAsync(Receipt("new", nowUtc.AddDays(-6)));

        var removed = await PurgePendingDeliveryReceiptsHandler.Handle(
            new PurgePendingDeliveryReceiptsCommand(7),
            _pending,
            CancellationToken.None
        );

        Assert.Equal(1, removed.Value);
        Assert.Equal("new", Assert.Single(_pending.Receipts).ProviderMessageId);
    }

    private static PendingDeliveryReceipt Receipt(string providerMessageId, DateTime receivedAtUtc) =>
        new(ProviderCode, providerMessageId, "dlr", nameof(SmsCanonicalStatus.Delivered), null, null, receivedAtUtc);

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
    public async Task Inbound_stop_opts_out_every_row_of_the_number_in_the_tenant()
    {
        var phone = PhoneE164.Create(Phone).Value;
        var optOuts = new FakeSmsOptOutRepository();
        var rowA = SmsOptOut.CreateSubscribed(Tenant, Customer, phone, DateTime.UtcNow);
        var rowB = SmsOptOut.CreateSubscribed(Tenant, Guid.NewGuid(), phone, DateTime.UtcNow);
        optOuts.Seed(rowA);
        optOuts.Seed(rowB);
        var provider = new FakeSmsProvider
        {
            InboundMessage = new SmsInboundMessage(
                Phone,
                SmsInboundKeyword.Stop,
                "STOP",
                "inbound",
                "in-4",
                Tenant,
                Customer
            ),
        };

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
        Assert.True(rowA.IsOptedOut);
        Assert.True(rowB.IsOptedOut);
        Assert.Empty(optOuts.Added);
    }

    [Fact]
    public async Task Inbound_stop_without_hint_applies_to_every_tenant_that_texted_the_number()
    {
        var tenantX = Guid.NewGuid();
        var tenantY = Guid.NewGuid();
        var messages = new FakeSmsMessageRepository();
        messages.SeedPhoneHistory(MessageTo(tenantX, Guid.NewGuid()));
        messages.SeedPhoneHistory(MessageTo(tenantY, Guid.NewGuid()));
        var provider = new FakeSmsProvider
        {
            InboundMessage = new SmsInboundMessage(
                Phone,
                SmsInboundKeyword.Stop,
                "STOP",
                "inbound",
                "in-5",
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
        Assert.Contains(optOuts.Added, o => o.TenantId == tenantX && o.IsOptedOut);
        Assert.Contains(optOuts.Added, o => o.TenantId == tenantY && o.IsOptedOut);
    }

    [Fact]
    public async Task InboundUnsubscribe_RecordsOptOut()
    {
        var messages = new FakeSmsMessageRepository();
        messages.SeedPhoneHistory(MessageTo(Tenant, Customer));
        // Adaptador real (fake de desarrollo): el texto pasa por su parser.
        var adapter = new TaxVision.Sms.Infrastructure.Providers.Fake.FakeSmsProvider(
            Microsoft.Extensions.Options.Options.Create(
                new TaxVision.Sms.Infrastructure.Providers.SmsProvidersOptions()
            ),
            NullLogger<TaxVision.Sms.Infrastructure.Providers.Fake.FakeSmsProvider>.Instance
        );
        var optOuts = new FakeSmsOptOutRepository();

        var result = await ProcessInboundHandler.Handle(
            new ProcessInboundCommand(
                ProviderCode,
                $"{{\"from\":\"{Phone}\",\"text\":\"Unsubscribe\",\"providerMessageId\":\"in-6\"}}",
                "sig"
            ),
            new FakeSmsAdapterFactory(adapter),
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
    }

    private static SmsMessage MessageTo(Guid tenantId, Guid customerId) =>
        SmsMessage
            .Create(
                tenantId,
                customerId,
                PhoneE164.Create(Phone).Value,
                SmsBody.Create("hi").Value,
                "idem-" + Guid.NewGuid().ToString("N"),
                "corr-1",
                Guid.NewGuid(),
                ProviderCode,
                "docs",
                [],
                DateTime.UtcNow
            )
            .Value;

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

    // ─────────── Proveedor desconocido (SMS-07) ───────────

    [Fact]
    public async Task Unknown_provider_returns_failure_without_throwing()
    {
        var adapters = new MapSmsAdapterFactory(new Dictionary<string, ISmsProvider>());

        var result = await ProcessDeliveryReceiptHandler.Handle(
            new ProcessDeliveryReceiptCommand("nope", "{}", "sig"),
            adapters,
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            new FakePendingDeliveryReceiptRepository(),
            new FakeSmsMessageRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            NullLogger<ProcessDeliveryReceiptCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("sms.webhook.unknownProvider", result.Error.Code);
    }

    [Fact]
    public async Task Inbound_from_unknown_provider_returns_failure_without_throwing()
    {
        var adapters = new MapSmsAdapterFactory(new Dictionary<string, ISmsProvider>());

        var result = await ProcessInboundHandler.Handle(
            new ProcessInboundCommand("nope", "{}", "sig"),
            adapters,
            new FakeSmsWebhookSecrets(),
            new FakeProcessedWebhookRepository(),
            new FakeSmsMessageRepository(),
            new FakeSmsOptOutRepository(),
            new FakeUnitOfWork(),
            NullLogger<ProcessInboundCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("sms.webhook.unknownProvider", result.Error.Code);
    }
}
