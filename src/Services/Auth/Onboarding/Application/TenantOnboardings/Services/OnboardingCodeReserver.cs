using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

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

    /// <param name="generation">0 la primera vez; la renovación de reservas vencidas pasa la siguiente.</param>
    public async Task<Result<OnboardingPricingOutcome>> ReserveAsync(
        TenantOnboarding onboarding,
        IReadOnlyList<OnboardingCodeInput> codes,
        CancellationToken ct,
        int generation = 0
    )
    {
        var priceResult = await pricing.GetGrossPriceAsync(onboarding.PlanId, onboarding.BillingCycle, ct);
        if (priceResult.IsFailure)
            return Result.Failure<OnboardingPricingOutcome>(priceResult.Error);

        var gross = priceResult.Value.GrossPriceCents;
        var currency = priceResult.Value.Currency;

        long residual = gross;
        var reservations = new List<OnboardingCodeReservationInput>();
        var applied = new List<Guid>();

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
                    QuoteTtlSeconds,
                    generation,
                    BuyerEmail: onboarding.Email,
                    BillingCycle: onboarding.BillingCycle,
                    StackedWithCodeDefinitionIds: [.. applied]
                ),
                ct
            );
            if (quote.IsFailure)
                return Result.Failure<OnboardingPricingOutcome>(quote.Error);

            var paymentReferenceId = OnboardingPaymentReference.For(onboarding.Id, order, generation);
            var reserve = await growth.ReserveAsync(
                quote.Value.QuoteId,
                paymentReferenceId,
                ReservationTtlSeconds,
                idempotencyKey: generation == 0
                    ? $"onb-reserve:{onboarding.Id:N}:{snapshot}"
                    : $"onb-reserve:{onboarding.Id:N}:{snapshot}:{generation}",
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
                    snapshot,
                    reserve.Value.ExpiresAtUtc,
                    paymentReferenceId,
                    RecurringOf(quote.Value, currency),
                    quote.Value.CodeDisplay
                )
            );
            residual = reserve.Value.NetAmountCents;
            if (quote.Value.CodeDefinitionId is { } reservedCode)
                applied.Add(reservedCode);
            order++;
        }

        var totalDiscount = gross - residual;
        return Result.Success(new OnboardingPricingOutcome(gross, totalDiscount, residual, currency, reservations));
    }

    /// <summary>
    /// Cotiza (SIN reservar) el precio con los códigos: el mismo orden y el mismo residual que <see cref="ReserveAsync"/>,
    /// para que lo que se muestra sea lo que se cobra. Un código que no sirve no rompe la vista previa: queda marcado
    /// como inválido y no descuenta. Cada cotización usa un sujeto efímero propio (la vista previa aún no tiene alta).
    /// </summary>
    public async Task<Result<OnboardingPricingPreviewOutcome>> PreviewAsync(
        Guid planId,
        string billingCycle,
        IReadOnlyList<OnboardingCodeInput> codes,
        CancellationToken ct,
        string? buyerEmail = null
    )
    {
        var priceResult = await pricing.GetGrossPriceAsync(planId, billingCycle, ct);
        if (priceResult.IsFailure)
            return Result.Failure<OnboardingPricingPreviewOutcome>(priceResult.Error);

        var gross = priceResult.Value.GrossPriceCents;
        var currency = priceResult.Value.Currency;
        var residual = gross;
        var lines = new List<Pricing.OnboardingPricingPreviewLine>();
        var applied = new List<Guid>();
        var subject = Guid.NewGuid();

        foreach (var code in codes.OrderBy(c => (int)c.BenefitType))
        {
            var hint = Hint(code.CodeToken);
            if (residual <= 0)
            {
                lines.Add(new(code.BenefitType.ToString(), hint, false, 0, "Onboarding.CodeExhausted"));
                continue;
            }

            var snapshot = ComputeSnapshot(subject, planId, residual, currency);
            var quote = await growth.QuoteAsync(
                new GrowthQuoteRequest(
                    code.CodeToken,
                    subject,
                    planId,
                    PlanVersion,
                    residual,
                    currency,
                    snapshot,
                    PreviewQuoteTtlSeconds,
                    BuyerEmail: buyerEmail,
                    BillingCycle: billingCycle,
                    StackedWithCodeDefinitionIds: [.. applied]
                ),
                ct
            );
            if (quote.IsFailure)
            {
                lines.Add(new(code.BenefitType.ToString(), hint, false, 0, quote.Error.Code));
                continue;
            }

            lines.Add(
                new(
                    code.BenefitType.ToString(),
                    hint,
                    true,
                    quote.Value.DiscountAmountCents,
                    null,
                    quote.Value.PercentageBasisPoints,
                    quote.Value.FixedAmountCents,
                    quote.Value.Duration,
                    quote.Value.DurationMonths
                )
            );
            residual = quote.Value.NetAmountCents;
            if (quote.Value.CodeDefinitionId is { } quotedCode)
                applied.Add(quotedCode);
        }

        return Result.Success(new OnboardingPricingPreviewOutcome(gross, gross - residual, residual, currency, lines));
    }

    private const int PreviewQuoteTtlSeconds = 10 * 60;

    /// <summary>Pista del código sin exponerlo completo: primeros 4 caracteres.</summary>
    private static string Hint(string code) => code.Length <= 4 ? code : $"{code[..4]}…";

    private static string ComputeSnapshot(Guid onboardingId, Guid planId, long residualCents, string currency)
    {
        var canonical = $"{onboardingId:N}|{planId:N}|{residualCents}|{currency}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    // Un código de N meses o para siempre deja descuento en la suscripción; uno de un solo cobro, no.
    private static OnboardingRecurringDiscount? RecurringOf(GrowthQuoteResult quote, string currency) =>
        quote is { CodeDefinitionId: { } codeId, Duration: "Months" or "Forever" }
            ? new OnboardingRecurringDiscount(
                codeId,
                quote.PercentageBasisPoints,
                quote.FixedAmountCents,
                currency,
                quote.Duration,
                quote.DurationMonths
            )
            : null;
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

/// <summary>Resultado de la vista previa: totales y una línea por código (válido o no).</summary>
public sealed record OnboardingPricingPreviewOutcome(
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    IReadOnlyList<Pricing.OnboardingPricingPreviewLine> Lines
);
