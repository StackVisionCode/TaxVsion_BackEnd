using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Onboarding.Application;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// Reintento del pago del alta con la reserva del código ya vencida: se vuelve a reservar el mismo código
/// (referencia de pago nueva) y se cobra el mismo neto; si el código ya no vale o el descuento cambió, no se cobra.
/// </summary>
public sealed class StartOnboardingCheckoutReservationRenewalTests
{
    private const long Gross = 12_900;
    private const long Discount = 2_580;

    private static readonly OnboardingOptions Options_ = new()
    {
        RegistrationUrlBase = "https://app.example.com",
        PaymentRetryMaxAttempts = 3,
        PaymentRetryWindowHours = 72,
    };

    private sealed class Fixture
    {
        public required TenantOnboarding Onboarding { get; init; }
        public required Guid OriginalReservationId { get; init; }
        public required Guid OriginalPaymentReference { get; init; }
        public RenewingGrowth Growth { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();

        public FakePaymentAppOnboardingClient PaymentApp { get; } =
            new(
                Result.Success(
                    new PaymentAppCheckoutResult(
                        Guid.NewGuid(),
                        "https://checkout.example.com/retry",
                        "sess_retry",
                        DateTime.UtcNow.AddHours(1)
                    )
                )
            );

        /// <summary>Alta con un código aplicado, cuyo primer pago falló.</summary>
        public static Fixture AfterFailedPayment(TimeSpan reservationExpiresIn)
        {
            var now = DateTime.UtcNow;
            var onboarding = OnboardingTestFactory.NewOnboarding(now.AddHours(-30));
            var reservationId = Guid.NewGuid();
            var paymentReference = OnboardingPaymentReference.For(onboarding.Id, 0);
            onboarding.ApplyOnboardingPricing(
                Gross,
                Discount,
                Gross - Discount,
                "USD",
                [
                    new OnboardingCodeReservationInput(
                        reservationId,
                        OnboardingBenefitType.Promo,
                        "WELCOME20",
                        Discount,
                        "snap-1",
                        now.Add(reservationExpiresIn),
                        paymentReference
                    ),
                ],
                now.AddHours(-30)
            );
            onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_first");
            onboarding.MarkPaymentFailed("card_declined");
            return new Fixture
            {
                Onboarding = onboarding,
                OriginalReservationId = reservationId,
                OriginalPaymentReference = paymentReference,
            };
        }

        /// <summary>Alta con código promocional y de regalo, lista para pagar.</summary>
        public static Fixture WithPromoAndGift(Guid promo, Guid gift)
        {
            var now = DateTime.UtcNow;
            var onboarding = OnboardingTestFactory.NewOnboarding(now);
            onboarding.ApplyOnboardingPricing(
                Gross,
                Discount,
                Gross - Discount,
                "USD",
                [
                    new OnboardingCodeReservationInput(
                        promo,
                        OnboardingBenefitType.Promo,
                        "WELCOME20",
                        2_000,
                        "snap-promo",
                        now.AddHours(6),
                        OnboardingPaymentReference.For(onboarding.Id, 0)
                    ),
                    new OnboardingCodeReservationInput(
                        gift,
                        OnboardingBenefitType.Gift,
                        "GIFTCARD5",
                        Discount - 2_000,
                        "snap-gift",
                        now.AddHours(6),
                        OnboardingPaymentReference.For(onboarding.Id, 1)
                    ),
                ],
                now
            );
            return new Fixture
            {
                Onboarding = onboarding,
                OriginalReservationId = promo,
                OriginalPaymentReference = OnboardingPaymentReference.For(onboarding.Id, 0),
            };
        }

        public Task<Result<StartOnboardingCheckoutResponse>> RetryAsync() =>
            StartOnboardingCheckoutHandler.Handle(
                // La pantalla de reintento no vuelve a mandar los códigos.
                new StartOnboardingCheckoutCommand(
                    Onboarding.Id,
                    "buyer@example.com",
                    "https://app.example.com/success",
                    "https://app.example.com/cancel"
                ),
                new FakeTenantOnboardingRepository { Existing = Onboarding },
                new OnboardingCodeReserver(new FixedPricing(), Growth),
                Growth,
                NullLogger<StartOnboardingCheckoutCommand>.Instance,
                new OnboardingSuccessCompleter(
                    new SecureTokenService(),
                    new FakeTokenReferenceStore(),
                    Options.Create(Options_),
                    FixedPlatformSettings.For("taxoffix.io"),
                    NullLogger<OnboardingSuccessCompleter>.Instance
                ),
                new FakePlanCatalogClient("Enterprise"),
                PaymentApp,
                new FakeOnboardingReturnReferenceStore(),
                Options.Create(Options_),
                UnitOfWork,
                new FakeMessageBus(),
                new FakeCorrelationContext(),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task Retry_after_the_reservation_expired_reserves_again_and_charges_the_same_net()
    {
        var fixture = Fixture.AfterFailedPayment(TimeSpan.FromHours(-6));

        var result = await fixture.RetryAsync();

        Assert.True(result.IsSuccess);
        // Cotización nueva: la vieja ya venció y Growth la devolvería tal cual por idempotencia.
        var quote = Assert.Single(fixture.Growth.Quotes);
        Assert.Equal(1, quote.Generation);
        // Los límites por persona cuentan con el email del alta.
        Assert.Equal(fixture.Onboarding.Email, quote.BuyerEmail);
        var reserve = Assert.Single(fixture.Growth.Reserves);
        Assert.NotEqual(fixture.OriginalPaymentReference, reserve.PaymentReferenceId);
        Assert.EndsWith(":1", reserve.IdempotencyKey);
        var reservation = Assert.Single(fixture.Onboarding.CodeReservations);
        Assert.Equal(reserve.ReservationId, reservation.CodeReservationId);
        Assert.Equal(reserve.PaymentReferenceId, reservation.PaymentReferenceId);
        Assert.Equal(Gross - Discount, fixture.PaymentApp.LastRequest!.NetAmountCents);
        Assert.Equal(reserve.ReservationId, fixture.PaymentApp.LastRequest.CodeReservationId);
        // La reserva vieja se libera en Growth con la referencia con la que se creó.
        var cancel = Assert.Single(fixture.Growth.Cancels);
        Assert.Equal(fixture.OriginalReservationId, cancel.ReservationId);
        Assert.Equal(fixture.OriginalPaymentReference, cancel.PaymentReferenceId);
    }

    [Fact]
    public async Task The_checkout_sends_every_code_reservation_to_payment_app()
    {
        var promo = Guid.NewGuid();
        var gift = Guid.NewGuid();
        var fixture = Fixture.WithPromoAndGift(promo, gift);

        var result = await fixture.RetryAsync();

        // Cada canje se podrá compensar si el pago se reembolsa, no solo el primero.
        Assert.True(result.IsSuccess);
        var promotions = fixture.PaymentApp.LastRequest!.Promotions!.OrderBy(p => p.Order).ToList();
        Assert.Equal([promo, gift], promotions.Select(p => p.CodeReservationId));
        Assert.Equal([2_000L, Discount - 2_000], promotions.Select(p => p.DiscountAmountCents));
        Assert.Equal(["snap-promo", "snap-gift"], promotions.Select(p => p.PromotionSnapshotHash));
    }

    [Fact]
    public async Task Retry_within_the_reservation_reuses_it()
    {
        var fixture = Fixture.AfterFailedPayment(TimeSpan.FromHours(6));

        var result = await fixture.RetryAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Growth.Reserves);
        Assert.Equal(fixture.OriginalReservationId, fixture.Onboarding.CodeReservations.Single().CodeReservationId);
        Assert.Equal(fixture.OriginalReservationId, fixture.PaymentApp.LastRequest!.CodeReservationId);
    }

    [Fact]
    public async Task Retry_with_a_code_no_longer_available_returns_conflict_without_charging()
    {
        var fixture = Fixture.AfterFailedPayment(TimeSpan.FromHours(-6));
        fixture.Growth.CodeAvailable = false;

        var result = await fixture.RetryAsync();

        Assert.Equal("Onboarding.CodeNoLongerAvailable", result.Error.Code);
        Assert.Null(fixture.PaymentApp.LastRequest);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Retry_with_a_different_discount_returns_conflict_without_charging()
    {
        var fixture = Fixture.AfterFailedPayment(TimeSpan.FromHours(-6));
        fixture.Growth.DiscountCents = 1_000;

        var result = await fixture.RetryAsync();

        Assert.Equal("Onboarding.CodeNoLongerAvailable", result.Error.Code);
        Assert.Null(fixture.PaymentApp.LastRequest);
        // La reserva nueva que no sirvió se libera.
        Assert.Contains(fixture.Growth.Cancels, c => c.ReservationId == fixture.Growth.Reserves.Single().ReservationId);
    }

    private sealed class FixedPricing : IOnboardingPlanPricingClient
    {
        public Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new OnboardingPlanPrice(Gross, "USD")));
    }

    private sealed record ReserveCall(Guid ReservationId, Guid PaymentReferenceId, string IdempotencyKey);

    private sealed record CancelCall(Guid ReservationId, Guid PaymentReferenceId);

    private sealed class RenewingGrowth : IGrowthOnboardingClient
    {
        public bool CodeAvailable { get; set; } = true;
        public long DiscountCents { get; set; } = Discount;
        public List<ReserveCall> Reserves { get; } = [];
        public List<CancelCall> Cancels { get; } = [];
        public List<GrowthQuoteRequest> Quotes { get; } = [];
        private long _lastNet;

        public Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default)
        {
            Quotes.Add(request);
            if (!CodeAvailable)
                return Task.FromResult(
                    Result.Failure<GrowthQuoteResult>(
                        new Error("Codes.CreateQuote.CodeNotFound", "No applicable code.")
                    )
                );
            _lastNet = request.GrossAmountCents - DiscountCents;
            return Task.FromResult(
                Result.Success(
                    new GrowthQuoteResult(
                        Guid.NewGuid(),
                        request.GrossAmountCents,
                        DiscountCents,
                        _lastNet,
                        request.Currency,
                        DateTime.UtcNow.AddMinutes(10)
                    )
                )
            );
        }

        public Task<Result<GrowthReserveResult>> ReserveAsync(
            Guid quoteId,
            Guid paymentReferenceId,
            int ttlSeconds,
            string idempotencyKey,
            CancellationToken ct = default
        )
        {
            var reservationId = Guid.NewGuid();
            Reserves.Add(new ReserveCall(reservationId, paymentReferenceId, idempotencyKey));
            return Task.FromResult(
                Result.Success(
                    new GrowthReserveResult(
                        reservationId,
                        DiscountCents,
                        _lastNet,
                        DateTime.UtcNow.AddSeconds(ttlSeconds)
                    )
                )
            );
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
        )
        {
            Cancels.Add(new CancelCall(reservationId, paymentReferenceId));
            return Task.FromResult(Result.Success());
        }
    }
}
