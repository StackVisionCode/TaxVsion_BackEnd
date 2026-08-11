using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

public sealed class SendSmsBatchHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private const string Phone = "+18095551234";

    private sealed class Harness
    {
        public FakeSmsMessageRepository Messages { get; } = new();
        public FakeSmsOptOutRepository OptOuts { get; } = new();
        public FakeSmsProvider Provider { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public SmsOptions Options { get; } = new() { DefaultProvider = "fake", MaxBatchSize = 1000 };

        /// <summary>Cadena de proveedores (failover). Vacía ⇒ solo <see cref="Provider"/>.</summary>
        public List<ISmsProvider> Order { get; } = [];

        public Task<BuildingBlocks.Results.Result<SendSmsBatchResponse>> Run(SendSmsBatchCommand command)
        {
            IReadOnlyList<ISmsProvider> order = Order.Count > 0 ? Order : [Provider];
            return SendSmsBatchHandler.Handle(
                command,
                Messages,
                OptOuts,
                new FakeSmsProviderRouter(order),
                Microsoft.Extensions.Options.Options.Create(Options),
                UnitOfWork,
                Bus,
                NullLogger<SendSmsBatchCommand>.Instance,
                CancellationToken.None
            );
        }
    }

    private static SmsSendItemDto Item(
        string to = Phone,
        string message = "hi",
        string? idempotencyKey = "idem-1",
        IReadOnlyList<SmsMediaDto>? media = null
    ) => new(Customer, to, message, media, idempotencyKey, "docs");

    private static SendSmsBatchCommand Batch(params SmsSendItemDto[] items) => new(Tenant, "corr-1", items);

    [Fact]
    public async Task Empty_batch_fails()
    {
        var result = await new Harness().Run(Batch());

        Assert.True(result.IsFailure);
        Assert.Equal("sms.emptyBatch", result.Error.Code);
    }

    [Fact]
    public async Task Batch_over_max_size_fails()
    {
        var h = new Harness();
        h.Options.MaxBatchSize = 1;

        var result = await h.Run(Batch(Item(idempotencyKey: "a"), Item(idempotencyKey: "b")));

        Assert.True(result.IsFailure);
        Assert.Equal("sms.batchTooLarge", result.Error.Code);
    }

    [Fact]
    public async Task Happy_path_accepts_persists_and_publishes()
    {
        var h = new Harness();

        var result = await h.Run(Batch(Item()));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Accepted", item.Status);
        Assert.NotNull(item.ProviderMessageId);
        Assert.Single(h.Messages.Added);
        Assert.Equal(SmsMessageStatus.Accepted, h.Messages.Added[0].Status);
        Assert.Equal(1, h.Provider.SendAsyncCallCount);
        Assert.NotNull(h.Bus.LastOfType<SmsMessageAcceptedIntegrationEvent>());
        Assert.Equal(1, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Opted_out_recipient_is_suppressed_and_not_sent()
    {
        var h = new Harness();
        var optOut = SmsOptOut.CreateSubscribed(Tenant, Customer, PhoneE164.Create(Phone).Value, DateTime.UtcNow);
        optOut.OptOut("STOP", DateTime.UtcNow);
        h.OptOuts.Seed(optOut);

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Suppressed", item.Status);
        Assert.Equal(0, h.Provider.SendAsyncCallCount); // never dispatched
        Assert.NotNull(h.Bus.LastOfType<SmsMessageSuppressedIntegrationEvent>());
    }

    [Fact]
    public async Task Existing_idempotency_key_returns_existing_without_resending()
    {
        var h = new Harness();
        var existing = SmsMessage.Create(
            Tenant, Customer, PhoneE164.Create(Phone).Value, SmsBody.Create("hi").Value,
            "idem-1", "corr-old", Guid.NewGuid(), "fake", "docs", [], DateTime.UtcNow
        ).Value;
        h.Messages.SeedForIdempotency(existing);

        var result = await h.Run(Batch(Item(idempotencyKey: "idem-1")));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal(existing.Id, item.MessageId);
        Assert.Equal(0, h.Provider.SendAsyncCallCount); // not resent
        Assert.Empty(h.Messages.Added); // nothing new persisted
    }

    [Fact]
    public async Task Media_not_supported_fails_explicitly_without_sending()
    {
        var h = new Harness();
        h.Provider.Capabilities = h.Provider.Capabilities with { SupportsMedia = false };
        var media = new List<SmsMediaDto> { new("https://x/y.pdf", "application/pdf", "y.pdf", 100) };

        var result = await h.Run(Batch(Item(media: media)));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Failed", item.Status);
        Assert.Equal(SmsErrors.MediaNotSupported.Code, item.ErrorCode);
        Assert.Equal(0, h.Provider.SendAsyncCallCount); // never degraded to text
        Assert.NotNull(h.Bus.LastOfType<SmsMessageFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Invalid_destination_fails_the_item_but_not_the_batch()
    {
        var h = new Harness();

        var result = await h.Run(Batch(Item(to: "not-a-phone")));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Failed", item.Status);
        Assert.Equal(SmsErrors.InvalidDestination.Code, item.ErrorCode);
        Assert.Null(item.MessageId);
    }

    [Fact]
    public async Task Provider_rejection_marks_failed_and_publishes_failed_event()
    {
        var h = new Harness();
        h.Provider.SendImpl = _ => new SmsSendResult(false, null, "providerRejected", "carrier down");

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Failed", item.Status);
        Assert.Equal("providerRejected", item.ErrorCode);
        Assert.NotNull(h.Bus.LastOfType<SmsMessageFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Partial_success_returns_independent_per_item_results()
    {
        var h = new Harness();

        var result = await h.Run(
            Batch(
                Item(to: Phone, idempotencyKey: "ok"),
                Item(to: "bad-phone", idempotencyKey: "bad")
            )
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Results.Count);
        Assert.Equal("Accepted", result.Value.Results[0].Status);
        Assert.Equal("Failed", result.Value.Results[1].Status);
        Assert.Equal(SmsErrors.InvalidDestination.Code, result.Value.Results[1].ErrorCode);
        // Both share the batch + correlation.
        Assert.All(result.Value.Results, _ => Assert.Equal("corr-1", result.Value.CorrelationId));
    }

    // ─────────── Failover de plataforma ───────────

    [Fact]
    public async Task Fails_over_to_secondary_when_primary_rejects()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider { Code = "p1", SendImpl = _ => new SmsSendResult(false, null, "providerRejected", "down") };
        var secondary = new FakeSmsProvider { Code = "p2" }; // default: accepts
        h.Order.Add(primary);
        h.Order.Add(secondary);

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Accepted", item.Status);
        Assert.Equal(1, primary.SendAsyncCallCount);
        Assert.Equal(1, secondary.SendAsyncCallCount);
        Assert.Equal("p2", h.Messages.Added[0].ProviderCode); // recorded the provider that actually sent
    }

    [Fact]
    public async Task Marks_failed_with_last_error_when_all_providers_reject()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider { Code = "p1", SendImpl = _ => new SmsSendResult(false, null, "providerRejected", "x") };
        var secondary = new FakeSmsProvider { Code = "p2", SendImpl = _ => new SmsSendResult(false, null, "providerUnavailable", "y") };
        h.Order.Add(primary);
        h.Order.Add(secondary);

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Failed", item.Status);
        Assert.Equal("providerUnavailable", item.ErrorCode); // last provider's error
        Assert.Equal(1, primary.SendAsyncCallCount);
        Assert.Equal(1, secondary.SendAsyncCallCount);
    }

    [Fact]
    public async Task Does_not_call_secondary_when_primary_accepts()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider { Code = "p1" }; // accepts
        var secondary = new FakeSmsProvider { Code = "p2" };
        h.Order.Add(primary);
        h.Order.Add(secondary);

        var result = await h.Run(Batch(Item()));

        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
        Assert.Equal(1, primary.SendAsyncCallCount);
        Assert.Equal(0, secondary.SendAsyncCallCount); // no wasted call
        Assert.Equal("p1", h.Messages.Added[0].ProviderCode);
    }

    [Fact]
    public async Task Fails_over_when_primary_cannot_carry_media()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider
        {
            Code = "p1",
            Capabilities = FakeSmsProvider.FullCapabilities() with { SupportsMedia = false },
        };
        var secondary = new FakeSmsProvider { Code = "p2" }; // supports media (default)
        h.Order.Add(primary);
        h.Order.Add(secondary);
        var media = new List<SmsMediaDto> { new("https://x/y.pdf", "application/pdf", "y.pdf", 100) };

        var result = await h.Run(Batch(Item(media: media)));

        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
        Assert.Equal(0, primary.SendAsyncCallCount); // media invalid for p1 → skipped, never sent as text
        Assert.Equal(1, secondary.SendAsyncCallCount);
        Assert.Equal("p2", h.Messages.Added[0].ProviderCode);
    }
}
