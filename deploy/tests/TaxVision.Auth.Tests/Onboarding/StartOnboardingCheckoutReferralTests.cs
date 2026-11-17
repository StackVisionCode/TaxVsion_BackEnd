using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
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
/// Regla de producto: el código de referido SOLO identifica quién recomendó (premia al referidor, no al referido).
/// Lo declara a Growth por EVENTO (sin llamada M2M) y NUNCA se cotiza ni se reserva como descuento: el referido
/// paga el precio completo.
/// </summary>
public sealed class StartOnboardingCheckoutReferralTests
{
    private static readonly OnboardingOptions Options_ = new() { RegistrationUrlBase = "https://app.example.com" };

    [Fact]
    public async Task A_referral_code_is_declared_by_event_and_the_referee_still_pays_the_full_price()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(referralCode: " TVR-7BBB2F5055 ", channel: "whatsapp", shareId: f.ShareId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FullyCovered);
        var declared = Assert.Single(f.Bus.Published.OfType<OnboardingReferralDeclaredIntegrationEvent>());
        Assert.Equal(f.Onboarding.Id, declared.OnboardingId);
        Assert.Equal("TVR-7BBB2F5055", declared.ReferralCode);
        Assert.Equal("whatsapp", declared.Channel);
        Assert.Equal(f.ShareId, declared.ShareId);
        Assert.Equal(PlatformTenant.Id, declared.TenantId);
        Assert.Equal(PlatformTenant.Id.ToString(), f.Bus.TenantIdAtFirstPublish);
        // El referral NO es un descuento: no se cotizó ni se reservó nada, y no hay desglose de descuento.
        Assert.Empty(f.Growth.QuoteCalls);
        Assert.Empty(f.Onboarding.CodeReservations);
        Assert.Null(f.Onboarding.TotalDiscountCents);
        Assert.Equal(0, f.PaymentApp.LastRequest!.DiscountAmountCents ?? 0);
        Assert.Null(f.PaymentApp.LastRequest.CodeReservationId);
    }

    [Fact]
    public async Task The_payment_is_never_gated_on_the_referral_and_there_is_no_synchronous_growth_call()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(referralCode: "TVR-OLD0000000");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentProcessing, f.Onboarding.Status);
        Assert.NotNull(f.PaymentApp.LastRequest);
        Assert.Empty(f.Growth.QuoteCalls);
    }

    [Fact]
    public async Task Without_a_referral_code_no_event_is_declared()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(referralCode: null);

        Assert.True(result.IsSuccess);
        Assert.Empty(f.Bus.Published.OfType<OnboardingReferralDeclaredIntegrationEvent>());
    }

    [Theory]
    [InlineData("TVR-7BBB2F5055", true)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public async Task The_payment_request_says_whether_a_referral_was_declared(string? referralCode, bool declared)
    {
        var f = Fixture.Create();

        await f.RunAsync(referralCode: referralCode);

        // Viaja en el evento de pago: Growth reintenta si la atribución todavía no llegó.
        Assert.Equal(declared, f.PaymentApp.LastRequest!.ReferralDeclared);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("TVR 7BBB2F5055")]
    [InlineData("TVR-7BBB2F5055-TVR-7BBB2F5055-TVR-7BBB2F5055-TVR-7BBB2F5055-TVR")]
    public async Task A_referral_code_edited_into_an_impossible_shape_is_not_declared(string referralCode)
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(referralCode: referralCode);

        // El alta sigue a precio completo; Growth nunca recibe basura.
        Assert.True(result.IsSuccess);
        Assert.Empty(f.Bus.Published.OfType<OnboardingReferralDeclaredIntegrationEvent>());
        Assert.False(f.PaymentApp.LastRequest!.ReferralDeclared);
    }

    [Theory]
    [InlineData("WhatsApp!!")]
    [InlineData("x")]
    [InlineData("averyveryverylongchannelname")]
    public async Task A_channel_edited_in_the_link_is_dropped(string channel)
    {
        var f = Fixture.Create();

        await f.RunAsync(referralCode: "TVR-7BBB2F5055", channel: channel);

        var declared = Assert.Single(f.Bus.Published.OfType<OnboardingReferralDeclaredIntegrationEvent>());
        Assert.Null(declared.Channel);
    }

    [Fact]
    public async Task A_blank_channel_is_dropped_instead_of_sent()
    {
        var f = Fixture.Create();

        await f.RunAsync(referralCode: "TVR-7BBB2F5055", channel: "  ");

        var declared = Assert.Single(f.Bus.Published.OfType<OnboardingReferralDeclaredIntegrationEvent>());
        Assert.Null(declared.Channel);
    }

    private sealed class Fixture
    {
        public required TenantOnboarding Onboarding { get; init; }
        public required FakePaymentAppOnboardingClient PaymentApp { get; init; }
        public RecordingGrowthClient Growth { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public Guid ShareId { get; } = Guid.NewGuid();

        public static Fixture Create()
        {
            var now = DateTime.UtcNow;
            var onboarding = OnboardingTestFactory.NewOnboarding(now);
            var paymentApp = new FakePaymentAppOnboardingClient(
                Result.Success(
                    new PaymentAppCheckoutResult(
                        Guid.NewGuid(),
                        "https://checkout.example.com/session",
                        "sess_123",
                        now.AddHours(1)
                    )
                )
            );
            return new Fixture { Onboarding = onboarding, PaymentApp = paymentApp };
        }

        public Task<Result<StartOnboardingCheckoutResponse>> RunAsync(
            string? referralCode,
            string? channel = null,
            Guid? shareId = null
        ) =>
            StartOnboardingCheckoutHandler.Handle(
                new StartOnboardingCheckoutCommand(
                    Onboarding.Id,
                    "buyer@example.com",
                    "https://app.example.com/success",
                    "https://app.example.com/cancel",
                    ReferralCode: referralCode,
                    ReferralChannel: channel,
                    ReferralShareId: shareId
                ),
                new FakeTenantOnboardingRepository { Existing = Onboarding },
                new OnboardingCodeReserver(new NoPricing(), Growth),
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
                new FakeUnitOfWork(),
                Bus,
                new FakeCorrelationContext(),
                CancellationToken.None
            );
    }

    private sealed class NoPricing : IOnboardingPlanPricingClient
    {
        public Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("A referral code must not trigger pricing for discounts.");
    }

    private sealed class RecordingGrowthClient : IGrowthOnboardingClient
    {
        public List<GrowthQuoteRequest> QuoteCalls { get; } = [];

        public Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default)
        {
            QuoteCalls.Add(request);
            throw new InvalidOperationException("A referral code must not be quoted as a discount.");
        }

        public Task<Result<GrowthReserveResult>> ReserveAsync(
            Guid quoteId,
            Guid paymentReferenceId,
            int ttlSeconds,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("A referral code must not be reserved as a discount.");

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
