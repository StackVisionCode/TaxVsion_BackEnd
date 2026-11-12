using BuildingBlocks.Results;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Onboarding.Application.Pricing;

/// <summary>
/// Vista previa del precio del alta ANTES de pagar: bruto del plan, descuento de cada código (promo y gift) y neto.
/// Cotiza contra Growth sin reservar nada, así que el comprador ve el nuevo total y si un código no sirve. El código de
/// referido no aparece aquí: no es un descuento (solo atribuye).
/// </summary>
public sealed record GetOnboardingPricingPreviewQuery(
    Guid PlanId,
    string BillingCycle,
    string? PromoCode,
    string? GiftCode
);

public sealed record OnboardingPricingPreviewLine(
    string Kind,
    string CodeHint,
    bool Valid,
    long DiscountAmountCents,
    string? ErrorCode
);

public sealed record OnboardingPricingPreview(
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    bool FullyCovered,
    IReadOnlyList<OnboardingPricingPreviewLine> Lines
);

public static class GetOnboardingPricingPreviewHandler
{
    public static async Task<Result<OnboardingPricingPreview>> Handle(
        GetOnboardingPricingPreviewQuery query,
        OnboardingCodeReserver reserver,
        CancellationToken ct
    )
    {
        if (query.PlanId == Guid.Empty)
        {
            return Result.Failure<OnboardingPricingPreview>(
                new Error("Onboarding.InvalidPlan", "PlanId is required.")
            );
        }

        var codes = new List<OnboardingCodeInput>();
        if (!string.IsNullOrWhiteSpace(query.PromoCode))
            codes.Add(new OnboardingCodeInput(query.PromoCode.Trim(), OnboardingBenefitType.Promo, query.PromoCode.Trim()));
        if (!string.IsNullOrWhiteSpace(query.GiftCode))
            codes.Add(new OnboardingCodeInput(query.GiftCode.Trim(), OnboardingBenefitType.Gift, query.GiftCode.Trim()));

        var preview = await reserver.PreviewAsync(query.PlanId, query.BillingCycle, codes, ct);
        if (preview.IsFailure)
            return Result.Failure<OnboardingPricingPreview>(preview.Error);

        var outcome = preview.Value;
        return Result.Success(
            new OnboardingPricingPreview(
                outcome.GrossAmountCents,
                outcome.DiscountAmountCents,
                outcome.NetAmountCents,
                outcome.Currency,
                outcome.NetAmountCents <= 0,
                outcome.Lines
            )
        );
    }
}
