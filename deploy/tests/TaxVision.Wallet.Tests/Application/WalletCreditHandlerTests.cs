using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Wallet.Credits;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>
/// REF-2: crédito idempotente de premios por referencia opaca (el Wallet no conoce "referidos") y su reversión.
/// 1 USD = 1_000_000 micros.
/// </summary>
public sealed class WalletCreditHandlerTests
{
    private const long TwentyFiveDollars = 25_000_000;
    private const string Reference = "referral-reward";

    [Fact]
    public async Task Credit_creates_the_wallet_and_records_a_Reward_entry_with_a_deterministic_key()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var (wallets, unitOfWork) = (new FakeWalletRepository(), new FakeUnitOfWork());

        var result = await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasReplay);
        Assert.Equal("Reward", result.Value.Movement);
        Assert.Equal(TwentyFiveDollars, result.Value.AmountMicros);
        Assert.Equal(TwentyFiveDollars, result.Value.AvailableMicrosAfter);
        var wallet = Assert.Single(wallets.Wallets);
        Assert.Equal(TwentyFiveDollars, wallet.PostedMicros);
        var entry = Assert.Single(wallets.LedgerEntries);
        Assert.Equal(MovementType.Reward, entry.Movement);
        Assert.Equal($"wallet-credit:referral-reward:{referenceId:N}", entry.OperationKey);
        Assert.Equal(referenceId, entry.ReferenceId);
        Assert.Equal(1, unitOfWork.SaveCount);
        var published = Assert.Single(
            DomainEventReplay.Dispatch([wallet]).OfType<WalletBalanceChangedIntegrationEvent>()
        );
        Assert.Equal("reward", published.Reason);
    }

    [Fact]
    public async Task Credit_adds_to_the_existing_balance_of_the_beneficiary_tenant_only()
    {
        var beneficiary = Guid.NewGuid();
        var other = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var beneficiaryWallet = WalletAggregate.Create(beneficiary).Value;
        beneficiaryWallet.Credit(Usd(100_000_000), "topup:seed");
        var otherWallet = WalletAggregate.Create(other).Value;
        wallets.Wallets.AddRange([beneficiaryWallet, otherWallet]);

        var result = await CreditAsync(beneficiary, Guid.NewGuid(), TwentyFiveDollars, wallets, new FakeUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.Equal(125_000_000, beneficiaryWallet.PostedMicros);
        Assert.Equal(0, otherWallet.PostedMicros);
    }

    [Fact]
    public async Task Credit_is_idempotent_a_retry_returns_the_same_entry_and_does_not_credit_twice()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var (wallets, unitOfWork) = (new FakeWalletRepository(), new FakeUnitOfWork());

        var first = await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);
        var retry = await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.Value.WasReplay);
        Assert.Equal(first.Value.LedgerEntryId, retry.Value.LedgerEntryId);
        Assert.Single(wallets.LedgerEntries);
        Assert.Equal(TwentyFiveDollars, wallets.Wallets.Single().PostedMicros);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Credit_for_the_same_reference_with_another_amount_is_a_conflict()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());

        var conflict = await CreditAsync(tenantId, referenceId, 30_000_000, wallets, new FakeUnitOfWork());

        Assert.True(conflict.IsFailure);
        Assert.Equal("Wallet.CreditConflict", conflict.Error.Code);
        Assert.Equal(TwentyFiveDollars, wallets.Wallets.Single().PostedMicros);
    }

    [Fact]
    public async Task The_same_reference_credits_each_tenant_independently()
    {
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();

        var a = await CreditAsync(Guid.NewGuid(), referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());
        var b = await CreditAsync(Guid.NewGuid(), referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());

        Assert.False(a.Value.WasReplay);
        Assert.False(b.Value.WasReplay);
        Assert.Equal(2, wallets.LedgerEntries.Count);
    }

    [Theory]
    [InlineData(0L, "USD", "Wallet.AmountNotPositive")]
    [InlineData(-5L, "USD", "Wallet.AmountNotPositive")]
    [InlineData(1_000_000L, "EUR", "Wallet.CurrencyMismatch")]
    [InlineData(1_000_000L, "", "Wallet.CurrencyMismatch")]
    public async Task Credit_rejects_invalid_amounts_and_non_USD_currencies(long micros, string currency, string code)
    {
        var wallets = new FakeWalletRepository();

        var result = await HandleCreditAsync(
            new CreditWalletCommand(Guid.NewGuid(), Reference, Guid.NewGuid(), micros, currency, null),
            wallets,
            new FakeUnitOfWork()
        );

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Empty(wallets.LedgerEntries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Has Spaces")]
    [InlineData("-leading-dash")]
    public async Task Credit_rejects_a_missing_or_malformed_reference_type(string referenceType)
    {
        var result = await HandleCreditAsync(
            new CreditWalletCommand(Guid.NewGuid(), referenceType, Guid.NewGuid(), TwentyFiveDollars, "USD", null),
            new FakeWalletRepository(),
            new FakeUnitOfWork()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Wallet.InvalidReferenceType", result.Error.Code);
    }

    [Fact]
    public async Task Credit_requires_a_tenant_and_a_reference_id()
    {
        var noTenant = await HandleCreditAsync(
            new CreditWalletCommand(Guid.Empty, Reference, Guid.NewGuid(), TwentyFiveDollars, "USD", null),
            new FakeWalletRepository(),
            new FakeUnitOfWork()
        );
        var noReference = await HandleCreditAsync(
            new CreditWalletCommand(Guid.NewGuid(), Reference, Guid.Empty, TwentyFiveDollars, "USD", null),
            new FakeWalletRepository(),
            new FakeUnitOfWork()
        );

        Assert.Equal(WalletErrors.TenantRequired.Code, noTenant.Error.Code);
        Assert.Equal("Wallet.InvalidReference", noReference.Error.Code);
    }

    [Fact]
    public async Task Credit_is_allowed_on_a_frozen_wallet_because_it_never_takes_money_out()
    {
        var tenantId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Freeze();
        wallets.Wallets.Add(wallet);

        var result = await CreditAsync(tenantId, Guid.NewGuid(), TwentyFiveDollars, wallets, new FakeUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.Equal(TwentyFiveDollars, wallet.PostedMicros);
    }

    [Fact]
    public async Task Reverse_takes_back_the_reward_with_its_own_idempotent_key()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var (wallets, unitOfWork) = (new FakeWalletRepository(), new FakeUnitOfWork());
        await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);

        var reversed = await ReverseAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);
        var retry = await ReverseAsync(tenantId, referenceId, TwentyFiveDollars, wallets, unitOfWork);

        Assert.True(reversed.IsSuccess);
        Assert.Equal("RewardReversal", reversed.Value.Movement);
        Assert.Equal(0, wallets.Wallets.Single().PostedMicros);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.Value.WasReplay);
        Assert.Equal(2, wallets.LedgerEntries.Count);
        Assert.Equal($"wallet-clawback:referral-reward:{referenceId:N}", wallets.LedgerEntries[1].OperationKey);
    }

    [Fact]
    public async Task Reverse_without_a_prior_credit_or_above_the_credited_amount_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();

        var withoutCredit = await ReverseAsync(tenantId, referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());
        await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());
        var tooMuch = await ReverseAsync(tenantId, referenceId, 30_000_000, wallets, new FakeUnitOfWork());

        Assert.Equal("Wallet.CreditNotFound", withoutCredit.Error.Code);
        Assert.Equal("Wallet.ReversalExceedsCredit", tooMuch.Error.Code);
        Assert.Equal(TwentyFiveDollars, wallets.Wallets.Single().PostedMicros);
    }

    [Fact]
    public async Task Reverse_never_leaves_the_wallet_negative_or_below_the_held_amount()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        await CreditAsync(tenantId, referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());
        var wallet = wallets.Wallets.Single();
        // El referidor ya gastó/retuvo casi todo el premio: solo quedan 5 USD disponibles.
        wallet.Reserve(Usd(20_000_000), "reserve:campaign");

        var result = await ReverseAsync(tenantId, referenceId, TwentyFiveDollars, wallets, new FakeUnitOfWork());

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.InsufficientFunds.Code, result.Error.Code);
        Assert.Equal(TwentyFiveDollars, wallet.PostedMicros);
        Assert.Equal(20_000_000, wallet.HeldMicros);
        // La reversión rechazada no agrega asiento: solo queda el del crédito.
        var onlyEntry = Assert.Single(wallets.LedgerEntries);
        Assert.Equal(MovementType.Reward, onlyEntry.Movement);
    }

    private static Task<BuildingBlocks.Results.Result<WalletCreditView>> CreditAsync(
        Guid tenantId,
        Guid referenceId,
        long micros,
        FakeWalletRepository wallets,
        FakeUnitOfWork unitOfWork
    ) =>
        HandleCreditAsync(
            new CreditWalletCommand(tenantId, Reference, referenceId, micros, "USD", "referral reward"),
            wallets,
            unitOfWork
        );

    private static Task<BuildingBlocks.Results.Result<WalletCreditView>> HandleCreditAsync(
        CreditWalletCommand command,
        FakeWalletRepository wallets,
        FakeUnitOfWork unitOfWork
    ) =>
        CreditWalletHandler.Handle(
            command,
            wallets,
            unitOfWork,
            NullLogger<CreditWalletCommand>.Instance,
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<WalletCreditView>> ReverseAsync(
        Guid tenantId,
        Guid referenceId,
        long micros,
        FakeWalletRepository wallets,
        FakeUnitOfWork unitOfWork
    ) =>
        ReverseWalletCreditHandler.Handle(
            new ReverseWalletCreditCommand(tenantId, Reference, referenceId, micros, "USD", "refund in window"),
            wallets,
            unitOfWork,
            NullLogger<ReverseWalletCreditCommand>.Instance,
            CancellationToken.None
        );

    private static TaxVision.Wallet.Domain.ValueObjects.WalletMoney Usd(long micros) =>
        TaxVision.Wallet.Domain.ValueObjects.WalletMoney.Create(micros, "USD").Value;
}
