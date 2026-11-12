using BuildingBlocks.Results;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Pricing;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// La vista previa del precio muestra bruto → descuento → neto con los mismos órdenes y residuales que la reserva,
/// pero SIN reservar nada, y un código inválido no rompe la vista previa.
/// </summary>
public sealed class OnboardingPricingPreviewTests
{
    private static readonly Guid Plan = Guid.NewGuid();

    [Fact]
    public async Task A_gift_code_shows_gross_discount_and_net_without_reserving()
    {
        var growth = new FakeGrowth { Discounts = { ["GIFT25ABC"] = 2_500 } };

        var result = await Run(growth, gift: "GIFT25ABC");

        Assert.True(result.IsSuccess);
        Assert.Equal(12_900, result.Value.GrossAmountCents);
        Assert.Equal(2_500, result.Value.DiscountAmountCents);
        Assert.Equal(10_400, result.Value.NetAmountCents);
        Assert.False(result.Value.FullyCovered);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal("Gift", line.Kind);
        Assert.True(line.Valid);
        Assert.Equal(0, growth.Reservations);
    }

    [Fact]
    public async Task A_code_covering_everything_is_marked_fully_covered()
    {
        var growth = new FakeGrowth { Discounts = { ["WELCOME100"] = 12_900 } };

        var result = await Run(growth, gift: "WELCOME100");

        Assert.Equal(0, result.Value.NetAmountCents);
        Assert.True(result.Value.FullyCovered);
    }

    [Fact]
    public async Task An_invalid_code_is_reported_and_does_not_discount_or_fail_the_preview()
    {
        var growth = new FakeGrowth();

        var result = await Run(growth, gift: "NOPE12345");

        Assert.True(result.IsSuccess);
        Assert.Equal(12_900, result.Value.NetAmountCents);
        var line = Assert.Single(result.Value.Lines);
        Assert.False(line.Valid);
        Assert.Equal("Codes.CodeNotFound", line.ErrorCode);
    }

    [Fact]
    public async Task Promo_and_gift_stack_in_order_against_the_residual_and_the_full_code_is_never_echoed()
    {
        var growth = new FakeGrowth { Discounts = { ["WELCOME20"] = 2_580, ["GIFT25ABC"] = 2_500 } };

        var result = await Run(growth, promo: "WELCOME20", gift: "GIFT25ABC");

        Assert.Equal(5_080, result.Value.DiscountAmountCents);
        Assert.Equal(7_820, result.Value.NetAmountCents);
        Assert.Equal(["Promo", "Gift"], result.Value.Lines.Select(l => l.Kind));
        Assert.All(result.Value.Lines, line => Assert.DoesNotContain("WELCOME20", line.CodeHint + "x"));
        Assert.All(result.Value.Lines, line => Assert.EndsWith("…", line.CodeHint));
    }

    [Fact]
    public async Task Without_codes_the_net_is_the_gross()
    {
        var result = await Run(new FakeGrowth());

        Assert.Equal(12_900, result.Value.NetAmountCents);
        Assert.Empty(result.Value.Lines);
    }

    [Fact]
    public async Task A_missing_plan_is_rejected()
    {
        var result = await GetOnboardingPricingPreviewHandler.Handle(
            new GetOnboardingPricingPreviewQuery(Guid.Empty, "Monthly", null, null),
            new OnboardingCodeReserver(new FixedPricing(), new FakeGrowth()),
            CancellationToken.None
        );

        Assert.Equal("Onboarding.InvalidPlan", result.Error.Code);
    }

    private static Task<Result<OnboardingPricingPreview>> Run(FakeGrowth growth, string? promo = null, string? gift = null) =>
        GetOnboardingPricingPreviewHandler.Handle(
            new GetOnboardingPricingPreviewQuery(Plan, "Monthly", promo, gift),
            new OnboardingCodeReserver(new FixedPricing(), growth),
            CancellationToken.None
        );

    private sealed class FixedPricing : IOnboardingPlanPricingClient
    {
        public Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new OnboardingPlanPrice(12_900, "USD")));
    }

    private sealed class FakeGrowth : IGrowthOnboardingClient
    {
        public Dictionary<string, long> Discounts { get; } = [];
        public int Reservations { get; private set; }

        public Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default) =>
            Task.FromResult(
                Discounts.TryGetValue(request.CodeToken, out var discount)
                    ? Result.Success(
                        new GrowthQuoteResult(
                            Guid.NewGuid(),
                            request.GrossAmountCents,
                            Math.Min(discount, request.GrossAmountCents),
                            request.GrossAmountCents - Math.Min(discount, request.GrossAmountCents),
                            request.Currency,
                            DateTime.UtcNow.AddMinutes(10)
                        )
                    )
                    : Result.Failure<GrowthQuoteResult>(new Error("Codes.CodeNotFound", "unknown"))
            );

        public Task<Result<GrowthReserveResult>> ReserveAsync(
            Guid quoteId,
            Guid paymentReferenceId,
            int ttlSeconds,
            string idempotencyKey,
            CancellationToken ct = default
        )
        {
            Reservations++;
            throw new InvalidOperationException("A preview must never reserve.");
        }


        public Task<Result> CommitAsync(
            Guid reservationId,
            Guid paymentReferenceId,
            string snapshotHash,
            Guid sourceEventId,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> CancelAsync(
            Guid reservationId,
            Guid paymentReferenceId,
            string reason,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
