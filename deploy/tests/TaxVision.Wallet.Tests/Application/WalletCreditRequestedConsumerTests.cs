using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Wallet.Credits;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>
/// Otro servicio pide por EVENTO (sin M2M) acreditar o revertir dinero con una referencia opaca; el Wallet aplica el mismo
/// caso de uso de siempre y responde con <c>wallet.credit.applied.v1</c> sobre esa misma referencia. Idempotente.
/// </summary>
public sealed class WalletCreditRequestedConsumerTests
{
    private const long TwentyFiveDollars = 25_000_000;

    private static WalletCreditRequestedIntegrationEvent Request(
        Guid tenantId,
        Guid referenceId,
        long micros = TwentyFiveDollars,
        bool reverse = false,
        string currency = "USD"
    ) =>
        new()
        {
            TenantId = tenantId,
            ReferenceType = "referral-reward",
            ReferenceId = referenceId,
            AmountMicros = micros,
            Currency = currency,
            Reason = "test",
            Reverse = reverse,
        };

    private static Task<WalletCreditAppliedIntegrationEvent> Run(
        WalletCreditRequestedIntegrationEvent evt,
        FakeWalletRepository wallets
    ) =>
        WalletCreditRequestedConsumer.Handle(
            evt,
            wallets,
            new FakeUnitOfWork(),
            NullLogger<CreditWalletCommand>.Instance,
            NullLogger<ReverseWalletCreditCommand>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task A_credit_request_is_applied_and_answered_with_the_same_opaque_reference()
    {
        var tenant = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var wallets = new FakeWalletRepository();

        var answer = await Run(Request(tenant, reference), wallets);

        Assert.True(answer.Succeeded);
        Assert.False(answer.Reverse);
        Assert.Equal(tenant, answer.TenantId);
        Assert.Equal("referral-reward", answer.ReferenceType);
        Assert.Equal(reference, answer.ReferenceId);
        Assert.NotNull(answer.LedgerEntryId);
        Assert.Null(answer.ErrorCode);
        Assert.Equal(TwentyFiveDollars, Assert.Single(wallets.Wallets).PostedMicros);
    }

    [Fact]
    public async Task A_redelivered_request_never_credits_twice_and_reports_the_replay()
    {
        var tenant = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var first = await Run(Request(tenant, reference), wallets);

        var second = await Run(Request(tenant, reference), wallets);

        Assert.True(second.Succeeded);
        Assert.True(second.WasReplay);
        Assert.Equal(first.LedgerEntryId, second.LedgerEntryId);
        Assert.Equal(TwentyFiveDollars, Assert.Single(wallets.Wallets).PostedMicros);
    }

    [Fact]
    public async Task A_reversal_request_takes_the_credit_back()
    {
        var tenant = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        await Run(Request(tenant, reference), wallets);

        var answer = await Run(Request(tenant, reference, reverse: true), wallets);

        Assert.True(answer.Succeeded);
        Assert.True(answer.Reverse);
        Assert.Equal(0, Assert.Single(wallets.Wallets).PostedMicros);
    }

    [Fact]
    public async Task Reversing_a_credit_that_never_existed_answers_with_a_business_error_instead_of_failing()
    {
        var answer = await Run(Request(Guid.NewGuid(), Guid.NewGuid(), reverse: true), new FakeWalletRepository());

        Assert.False(answer.Succeeded);
        Assert.Equal("Wallet.CreditNotFound", answer.ErrorCode);
        Assert.Null(answer.LedgerEntryId);
    }

    [Fact]
    public async Task A_non_usd_request_is_answered_with_the_currency_error_and_nothing_is_credited()
    {
        var wallets = new FakeWalletRepository();

        var answer = await Run(Request(Guid.NewGuid(), Guid.NewGuid(), currency: "EUR"), wallets);

        Assert.False(answer.Succeeded);
        Assert.NotNull(answer.ErrorCode);
        Assert.Empty(wallets.LedgerEntries);
    }
}
