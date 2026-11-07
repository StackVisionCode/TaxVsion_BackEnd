using BuildingBlocks.Results;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

/// <summary>Caracterización de SMS-09: failover, reserva del Wallet y gate antes de sacarlos del handler.</summary>
public sealed partial class SendSmsBatchHandlerTests
{
    [Fact]
    public async Task Provider_that_returns_a_failed_result_fails_over()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider { Code = "p1", SendFailure = new Error("provider.down", "Down.") };
        var secondary = new FakeSmsProvider { Code = "p2" };
        h.Order.AddRange([primary, secondary]);

        var result = await h.Run(Batch(Item()));

        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
        Assert.Equal(1, secondary.SendAsyncCallCount);
    }

    [Fact]
    public async Task Failed_result_with_unknown_outcome_does_not_fail_over()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider
        {
            Code = "p1",
            SendFailure = new Error(SmsSendFailure.OutcomeUnknown, "Timeout."),
        };
        var secondary = new FakeSmsProvider { Code = "p2" };
        h.Order.AddRange([primary, secondary]);

        var result = await h.Run(Batch(Item()));

        Assert.Equal("OutcomeUnknown", Assert.Single(result.Value.Results).Status);
        Assert.Equal(0, secondary.SendAsyncCallCount);
    }

    [Fact]
    public async Task Unknown_outcome_keeps_the_provider_message_id()
    {
        var h = new Harness();
        h.Provider.SendImpl = _ => new SmsSendResult(false, "prov-kept", SmsSendFailure.OutcomeUnknown, "Timeout.");

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("OutcomeUnknown", item.Status);
        Assert.Equal("prov-kept", item.ProviderMessageId);
    }

    [Fact]
    public async Task Provider_that_throws_has_an_unknown_outcome_and_does_not_fail_over()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider { Code = "p1", SendImpl = _ => throw new HttpRequestException("Reset.") };
        var secondary = new FakeSmsProvider { Code = "p2" };
        h.Order.AddRange([primary, secondary]);

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("OutcomeUnknown", item.Status);
        Assert.Equal(SmsErrors.ProviderOutcomeUnknown.Code, item.ErrorCode);
        Assert.Equal(0, secondary.SendAsyncCallCount);
    }

    [Fact]
    public async Task Carrier_opt_out_does_not_fail_over()
    {
        var h = new Harness();
        var primary = new FakeSmsProvider
        {
            Code = "p1",
            SendImpl = _ => new SmsSendResult(false, null, SmsSendFailure.RecipientOptedOut, "Blocked."),
        };
        var secondary = new FakeSmsProvider { Code = "p2" };
        h.Order.AddRange([primary, secondary]);

        await h.Run(Batch(Item()));

        Assert.Equal(0, secondary.SendAsyncCallCount);
    }

    [Fact]
    public async Task Rejection_without_a_code_uses_the_provider_rejected_code()
    {
        var h = new Harness();
        h.Provider.SendImpl = _ => new SmsSendResult(false, null, null, null);

        var result = await h.Run(Batch(Item()));

        var item = Assert.Single(result.Value.Results);
        Assert.Equal("Failed", item.Status);
        Assert.Equal(SmsErrors.ProviderRejected.Code, item.ErrorCode);
    }

    // Se reserva por ítem, antes del gate: el no asignado y el inválido se liberan al liquidar.
    [Fact]
    public async Task Wallet_reserves_every_item_the_contact_policy_lets_through()
    {
        var h = new Harness();
        var sender = Guid.NewGuid();
        h.Visibility.Enabled = true;
        h.Assignments.Assigned.Add(Customer);
        var command = new SendSmsBatchCommand(
            Tenant,
            "corr-1",
            [
                Item(idempotencyKey: "mine"),
                new SmsSendItemDto(OtherCustomer, OtherPhone, "hi", null, "not-mine", "docs"),
                Item(to: "not-a-phone", idempotencyKey: "invalid"),
            ],
            ActorUserId: sender,
            CanViewAll: false,
            ChargeWallet: true,
            CallerBearerToken: "bearer-token",
            BatchKey: BatchKey
        );

        var result = await h.Run(command);

        Assert.Equal(3, Assert.Single(h.Wallet.Reservations).Units.Sms);
        Assert.Equal(1, Assert.Single(h.Wallet.Settlements).ConsumedUnits);
        Assert.Equal(1, h.Provider.SendAsyncCallCount);
        Assert.Equal(3, result.Value.Results.Count);
    }

    [Fact]
    public async Task Rejected_wallet_stops_before_reading_the_assignments()
    {
        var h = new Harness();
        h.Visibility.Enabled = true;
        h.Wallet.ReserveResult = h.Wallet.ReserveResult with { Authorized = false, DeficitMicros = 5 };
        var command = new SendSmsBatchCommand(
            Tenant,
            "corr-1",
            [Item()],
            ActorUserId: Guid.NewGuid(),
            CanViewAll: false,
            ChargeWallet: true,
            BatchKey: BatchKey
        );

        var result = await h.Run(command);

        Assert.True(result.IsFailure);
        Assert.False(h.Assignments.WasAsked);
    }

    [Fact]
    public async Task Unreachable_wallet_rejects_the_batch()
    {
        var h = new Harness();
        h.Wallet.ReserveResult = h.Wallet.ReserveResult with { Reachable = false };

        var result = await h.Run(ChargedBatch(Item()));

        Assert.Equal(SmsErrors.WalletUnavailable.Code, result.Error.Code);
        Assert.Equal(0, h.Provider.SendAsyncCallCount);
    }

    [Fact]
    public async Task Item_without_a_customer_fails_alone()
    {
        var h = new Harness();

        var result = await h.Run(
            Batch(new SmsSendItemDto(Guid.Empty, Phone, "hi", null, "no-customer", "docs"), Item())
        );

        Assert.Equal(SmsErrors.InvalidCustomer.Code, result.Value.Results[0].ErrorCode);
        Assert.Equal("Accepted", result.Value.Results[1].Status);
    }

    [Fact]
    public async Task Empty_body_fails_alone()
    {
        var h = new Harness();

        var result = await h.Run(Batch(Item(message: " ")));

        Assert.Equal(SmsErrors.InvalidBody.Code, Assert.Single(result.Value.Results).ErrorCode);
    }

    [Fact]
    public async Task Same_message_without_a_key_is_sent_once_in_a_batch()
    {
        var h = new Harness();

        var result = await h.Run(Batch(Item(idempotencyKey: null), Item(idempotencyKey: null)));

        Assert.Equal(1, h.Provider.SendAsyncCallCount);
        Assert.False(result.Value.Results[0].Reused);
        Assert.True(result.Value.Results[1].Reused);
        Assert.StartsWith("auto-", Assert.Single(h.Messages.Added).IdempotencyKey);
    }

    [Fact]
    public async Task Explicit_key_is_trimmed()
    {
        var h = new Harness();

        await h.Run(Batch(Item(idempotencyKey: "  key-1  ")));

        Assert.Equal("key-1", Assert.Single(h.Messages.Added).IdempotencyKey);
    }

    [Fact]
    public async Task Batch_key_gives_the_same_batch_id()
    {
        var h = new Harness();

        var result = await h.Run(ChargedBatch(Item()));

        Assert.Equal(SendSmsBatchHandler.BatchIdFor(Tenant, BatchKey), result.Value.BatchId);
        Assert.Equal(SendSmsBatchHandler.BatchIdFor(Tenant, $" {BatchKey} "), result.Value.BatchId);
    }

    [Fact]
    public async Task Blank_correlation_gets_a_generated_one()
    {
        var h = new Harness();

        var result = await h.Run(new SendSmsBatchCommand(Tenant, " ", [Item()], null, false));

        Assert.False(string.IsNullOrWhiteSpace(result.Value.CorrelationId));
    }

    [Fact]
    public async Task Empty_tenant_is_rejected()
    {
        var h = new Harness();

        var result = await h.Run(new SendSmsBatchCommand(Guid.Empty, "corr-1", [Item()], null, false));

        Assert.Equal(SmsErrors.InvalidTenant.Code, result.Error.Code);
    }
}
