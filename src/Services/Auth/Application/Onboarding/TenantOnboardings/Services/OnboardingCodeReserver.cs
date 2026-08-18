using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// Reserva SECUENCIAL apilada de códigos en Growth durante el onboarding (pre-tenant). Cotiza+reserva
/// cada código contra el RESIDUAL (bruto → tras código A queda residual A → código B contra A → …), en
/// orden referido→promo→gift. Así la suma de descuentos nunca supera el bruto por construcción, y cada
/// beneficio conserva su identidad. El bruto lo da Subscription (autoritativo). Cada reserva se liga al
/// OnboardingId (contrato pre-tenant: PlatformTenant + Anonymous(OnboardingId) los pone el cliente Growth).
/// </summary>
public sealed class OnboardingCodeReserver(IOnboardingPlanPricingClient pricing, IGrowthOnboardingClient growth)
{
    // TTL de la reserva = vida de la sesión de checkout (24h), para no expirar antes del pago.
    private const int ReservationTtlSeconds = 24 * 60 * 60;
    private const int QuoteTtlSeconds = 24 * 60 * 60;
    private const string PlanVersion = "1";

    public async Task<Result<OnboardingPricingOutcome>> ReserveAsync(
        TenantOnboarding onboarding,
        IReadOnlyList<OnboardingCodeInput> codes,
        CancellationToken ct
    )
    {
        var priceResult = await pricing.GetGrossPriceAsync(onboarding.PlanId, onboarding.BillingCycle, ct);
        if (priceResult.IsFailure)
            return Result.Failure<OnboardingPricingOutcome>(priceResult.Error);

        var gross = priceResult.Value.GrossPriceCents;
        var currency = priceResult.Value.Currency;

        long residual = gross;
        var reservations = new List<OnboardingCodeReservationInput>();

        // Orden de aplicación: referido(0) → promo(1) → gift(2). El gift (dinero) absorbe el residual final.
        // `order` es la posición secuencial (0,1,2…): identifica la reserva y deriva su PaymentId único en
        // Growth (OnboardingPaymentReference.For). DEBE coincidir con el Order que asigna ApplyOnboardingPricing.
        var order = 0;
        foreach (var code in codes.OrderBy(c => (int)c.BenefitType))
        {
            if (residual <= 0)
                return Result.Failure<OnboardingPricingOutcome>(
                    new Error(
                        "Onboarding.CodeExhausted",
                        $"The code '{code.Label}' cannot be applied: the amount is already fully covered."
                    )
                );

            var snapshot = ComputeSnapshot(onboarding.Id, onboarding.PlanId, residual, currency);

            var quote = await growth.QuoteAsync(
                new GrowthQuoteRequest(
                    code.CodeToken,
                    onboarding.Id,
                    onboarding.PlanId,
                    PlanVersion,
                    residual,
                    currency,
                    snapshot,
                    QuoteTtlSeconds
                ),
                ct
            );
            if (quote.IsFailure)
                return Result.Failure<OnboardingPricingOutcome>(quote.Error);

            var reserve = await growth.ReserveAsync(
                quote.Value.QuoteId,
                OnboardingPaymentReference.For(onboarding.Id, order),
                ReservationTtlSeconds,
                idempotencyKey: $"onb-reserve:{onboarding.Id:N}:{snapshot}",
                ct
            );
            if (reserve.IsFailure)
                return Result.Failure<OnboardingPricingOutcome>(reserve.Error);

            // El Order persistido (OnboardingCodeReservation.Order) lo asigna ApplyOnboardingPricing por
            // posición de lista = este mismo `order`, así que el commit reconstruye el mismo PaymentId.
            reservations.Add(
                new OnboardingCodeReservationInput(
                    reserve.Value.ReservationId,
                    code.BenefitType,
                    code.Label,
                    reserve.Value.DiscountAmountCents,
                    snapshot
                )
            );
            residual = reserve.Value.NetAmountCents;
            order++;
        }

        var totalDiscount = gross - residual;
        return Result.Success(new OnboardingPricingOutcome(gross, totalDiscount, residual, currency, reservations));
    }

    private static string ComputeSnapshot(Guid onboardingId, Guid planId, long residualCents, string currency)
    {
        var canonical = $"{onboardingId:N}|{planId:N}|{residualCents}|{currency}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Un código entrado por el usuario en el onboarding + su tipo de beneficio.</summary>
public sealed record OnboardingCodeInput(string CodeToken, OnboardingBenefitType BenefitType, string? Label);

/// <summary>Resultado de la reserva apilada: desglose comercial + las reservas para persistir/commitear.</summary>
public sealed record OnboardingPricingOutcome(
    long GrossAmountCents,
    long TotalDiscountCents,
    long NetAmountCents,
    string Currency,
    IReadOnlyList<OnboardingCodeReservationInput> Reservations
);
