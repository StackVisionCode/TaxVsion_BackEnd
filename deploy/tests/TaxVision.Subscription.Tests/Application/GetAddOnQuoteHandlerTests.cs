using BuildingBlocks.Results;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.AddOns.Queries;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Vista previa de la compra de un add-on (storage en el CRM): el prorrateo, el descuento vigente si cubre add-ons y el
/// neto, con la misma regla que el checkout. Lo que se ve es lo que se cobra.
/// </summary>
public sealed class GetAddOnQuoteHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Without_a_discount_the_net_is_the_prorated_price()
    {
        var f = new Fixture();

        var quote = await f.QuoteAsync(quantity: 1);

        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.True(quote.Value.GrossTotalCents > 0);
        Assert.Null(quote.Value.DiscountCents);
        Assert.Equal(quote.Value.GrossTotalCents, quote.Value.NetTotalCents);
        Assert.Equal(f.Definition.Name, quote.Value.Name);
    }

    [Fact]
    public async Task A_discount_that_covers_add_ons_lowers_the_net()
    {
        var f = new Fixture();
        f.Grant(DiscountAppliesTo.BaseSeatsAddOns);

        var quote = await f.QuoteAsync(quantity: 1);

        Assert.Equal(quote.Value.GrossTotalCents / 5, quote.Value.DiscountCents);
        Assert.Equal(quote.Value.GrossTotalCents - quote.Value.DiscountCents, quote.Value.NetTotalCents);
        Assert.Equal(2_000, quote.Value.DiscountPercentBasisPoints);
    }

    [Fact]
    public async Task A_discount_for_the_plan_only_does_not_touch_add_ons()
    {
        var f = new Fixture();
        f.Grant(DiscountAppliesTo.Base);

        var quote = await f.QuoteAsync(quantity: 1);

        Assert.Null(quote.Value.DiscountCents);
        Assert.Equal(quote.Value.GrossTotalCents, quote.Value.NetTotalCents);
    }

    [Fact]
    public async Task The_quote_matches_what_the_checkout_charges()
    {
        var f = new Fixture();
        f.Grant(DiscountAppliesTo.BaseSeatsAddOns);

        var quote = await f.QuoteAsync(quantity: 2);
        var reserved = await ReserveAddOnCheckoutHandler.Handle(
            new ReserveAddOnCheckoutCommand(
                new StartAddOnCheckoutCommand(
                    TenantId,
                    f.Definition.Code.Value,
                    2,
                    AutoRenew: true,
                    "owner@acme.test",
                    "https://app/ok",
                    "https://app/cancel",
                    "Stripe",
                    "Card",
                    Guid.NewGuid()
                )
            ),
            f.Eligibility(),
            new FakeAddOnPurchaseIntentRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(reserved.IsSuccess, reserved.IsFailure ? reserved.Error.Code : null);
        Assert.Equal(reserved.Value.TotalCents, quote.Value.NetTotalCents);
        Assert.Equal(reserved.Value.DiscountCents, quote.Value.DiscountCents);
    }

    [Fact]
    public async Task An_add_on_the_plan_already_includes_cannot_be_quoted()
    {
        var f = new Fixture(planModules: ["email"]);

        var quote = await f.QuoteAsync(quantity: 1);

        Assert.True(quote.IsFailure);
    }

    private sealed class Fixture
    {
        public SubscriptionPlan Plan { get; }
        public AddOnDefinition Definition { get; }
        public TenantSubscription Subscription { get; }

        public Fixture(string[]? planModules = null)
        {
            Plan = AddOnTestCatalog.PlanWith("starter", planModules ?? ["documents"]);
            Definition = AddOnTestCatalog.ModuleAddOn("module-addon-email", "email");
            Subscription = AddOnTestCatalog.ActiveSubscription(TenantId, Plan);
        }

        public void Grant(DiscountAppliesTo appliesTo)
        {
            var terms = SubscriptionDiscountTerms
                .Create(
                    DiscountSource.Admin,
                    null,
                    2_000,
                    null,
                    null,
                    DiscountDuration.Forever,
                    null,
                    appliesTo,
                    null,
                    "Retention deal",
                    DateTime.UtcNow.AddMinutes(-1)
                )
                .Value;
            Assert.True(Subscription.GrantDiscount(terms, false, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        }

        public AddOnPurchaseEligibility Eligibility() =>
            new(
                new FakeSubscriptionRepo(Subscription),
                new FakePlanRepo(Plan),
                new FakeModuleRepo(AddOnTestCatalog.SellableModule("email")),
                new FakeAddOnDefinitionRepository(Definition),
                new NoAddOnSettingsRepo(),
                new FakeTenantAddOnRepo([])
            );

        public Task<Result<AddOnQuoteResponse>> QuoteAsync(int quantity) =>
            GetAddOnQuoteHandler.Handle(
                new GetAddOnQuoteQuery(TenantId, Definition.Code.Value, quantity),
                Eligibility(),
                CancellationToken.None
            );
    }

    private sealed class NoAddOnSettingsRepo
        : TaxVision.Subscription.Application.Abstractions.ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
