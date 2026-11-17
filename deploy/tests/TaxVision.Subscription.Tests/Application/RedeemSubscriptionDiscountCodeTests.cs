using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Discounts;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using static TaxVision.Subscription.Tests.Application.SubscriptionDiscountAdminTests;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Una oficina con suscripción canjea un código: Subscription cotiza en Growth, reserva, confirma en el acto y
/// recién entonces guarda el descuento. Si la confirmación falla, cancela la reserva y no crea nada.
/// </summary>
public sealed class RedeemSubscriptionDiscountCodeTests
{
    private static readonly Guid Owner = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid CodeId = Guid.Parse("72000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Redeem_quotes_reserves_commits_and_creates_the_discount()
    {
        var f = new Fixture();

        var redeemed = await f.RedeemAsync(" spring25 ");

        Assert.True(redeemed.IsSuccess, redeemed.IsFailure ? redeemed.Error.Code : null);
        Assert.Equal(["quote", "reserve", "commit"], f.Growth.Calls);
        var quote = f.Growth.LastQuote!;
        Assert.Equal("spring25", quote.Code);
        Assert.Equal(f.Subscription.TenantId, quote.TenantId);
        Assert.Equal(f.Subscription.PlanId, quote.PlanId);
        Assert.Equal(2_900, quote.GrossAmountCents);
        Assert.Equal(f.Subscription.BillingCycle.ToString(), quote.BillingCycle);
        Assert.Equal(64, quote.SnapshotHash.Length);
        var discount = Assert.Single(f.Subscription.Discounts);
        Assert.Equal(DiscountSource.Code, discount.Source);
        Assert.Equal(CodeId, discount.CodeDefinitionId);
        Assert.Equal(2_500, discount.PercentBasisPoints);
        Assert.Equal(3, discount.Months);
        Assert.Equal("Promo code SPRI...0420 · 25% off", discount.ReceiptLabel());
        Assert.Equal(1, f.UnitOfWork.Saves);
        Assert.Equal("TenantSubscription.DiscountCodeRedeemed", Assert.Single(f.Audit.Entries).Action);
    }

    [Fact]
    public async Task Redeem_with_an_active_discount_returns_conflict()
    {
        var f = new Fixture();
        await f.RedeemAsync("first");
        f.Growth.Calls.Clear();

        var second = await f.RedeemAsync("second");

        Assert.Equal("Subscription.Discount.AlreadyActive", second.Error.Code);
        Assert.Empty(f.Growth.Calls);
        Assert.Single(f.Subscription.Discounts);
    }

    [Fact]
    public async Task Failed_commit_cancels_the_reservation_and_creates_nothing()
    {
        var f = new Fixture();
        f.Growth.CommitResult = Result.Failure(new Error("Codes.CodeReservation.Expired", "Expired."));

        var redeemed = await f.RedeemAsync("spring25");

        Assert.Equal("Codes.CodeReservation.Expired", redeemed.Error.Code);
        Assert.Equal(["quote", "reserve", "commit", "cancel"], f.Growth.Calls);
        Assert.Empty(f.Subscription.Discounts);
        Assert.Equal(0, f.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_code_growth_rejects_returns_its_error_and_reserves_nothing()
    {
        var f = new Fixture();
        f.Growth.QuoteResult = Result.Failure<GrowthCodeQuote>(
            new Error("Codes.CreateQuote.CodeNotFound", "No applicable code was found.")
        );

        var redeemed = await f.RedeemAsync("nope");

        Assert.Equal("Codes.CreateQuote.CodeNotFound", redeemed.Error.Code);
        Assert.Equal(["quote"], f.Growth.Calls);
        Assert.Empty(f.Subscription.Discounts);
    }

    [Fact]
    public async Task A_one_time_code_covers_the_next_renewal()
    {
        var f = new Fixture();
        f.Growth.QuoteResult = Result.Success(Quote("Once", months: null));

        var redeemed = await f.RedeemAsync("once");

        Assert.Equal(f.Subscription.CurrentPeriodEndUtc, redeemed.Value.StartsAtUtc);
        Assert.Equal(DiscountDuration.Once, redeemed.Value.Duration);
    }

    private static GrowthCodeQuote Quote(string duration, int? months) =>
        new(Guid.NewGuid(), CodeId, 2_500, null, "USD", duration, months, "SPRI...0420");

    private sealed class Fixture
    {
        private readonly FakePlans _plans;

        public Fixture()
        {
            var now = DateTime.UtcNow;
            var plan = SubscriptionPlan
                .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, now)
                .Value;
            var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
            version.AddPriceTier(
                PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
            );
            plan.AddVersion(version, Guid.Empty, now);
            plan.PublishVersion(version.Id, now, Guid.Empty, now);
            Subscription = TenantSubscription
                .ActivateImmediately(
                    Guid.NewGuid(),
                    plan,
                    version,
                    BillingCycle.Monthly,
                    now,
                    now.AddMonths(1),
                    Guid.Empty,
                    now
                )
                .Value;
            Repository = new SingleSubscriptionRepository(Subscription);
            _plans = new FakePlans(plan);
        }

        public TenantSubscription Subscription { get; }
        public SingleSubscriptionRepository Repository { get; }
        public FakeGrowth Growth { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public FixedCorrelation Correlation { get; } = new();

        public Task<Result<SubscriptionDiscountView>> RedeemAsync(string code) =>
            RedeemSubscriptionDiscountCodeHandler.Handle(
                new RedeemSubscriptionDiscountCodeCommand(Subscription.TenantId, code, Owner),
                Repository,
                _plans,
                Growth,
                UnitOfWork,
                Audit,
                Correlation,
                CancellationToken.None
            );
    }

    internal sealed class FakeGrowth : IGrowthCodeRedemptionClient
    {
        public List<string> Calls { get; } = [];
        public GrowthCodeQuoteRequest? LastQuote { get; private set; }
        public Result<GrowthCodeQuote> QuoteResult { get; set; } = Result.Success(Quote("Months", 3));
        public Result CommitResult { get; set; } = Result.Success();

        public Task<Result<GrowthCodeQuote>> QuoteAsync(GrowthCodeQuoteRequest request, CancellationToken ct)
        {
            Calls.Add("quote");
            LastQuote = request;
            return Task.FromResult(QuoteResult);
        }

        public Task<Result<Guid>> ReserveAsync(Guid tenantId, Guid quoteId, Guid redemptionId, CancellationToken ct)
        {
            Calls.Add("reserve");
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        public Task<Result> CommitAsync(
            Guid tenantId,
            Guid reservationId,
            Guid redemptionId,
            string snapshotHash,
            CancellationToken ct
        )
        {
            Calls.Add("commit");
            return Task.FromResult(CommitResult);
        }

        public Task<Result> CancelAsync(
            Guid tenantId,
            Guid reservationId,
            Guid redemptionId,
            string reason,
            CancellationToken ct
        )
        {
            Calls.Add("cancel");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakePlans(SubscriptionPlan plan) : IPlanRepository
    {
        public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionPlan>> GetOnSaleAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionPlan?>(plan.Id == planId ? plan : null);

        public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(SubscriptionPlan plan, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default) => Task.FromResult(false);
    }
}
