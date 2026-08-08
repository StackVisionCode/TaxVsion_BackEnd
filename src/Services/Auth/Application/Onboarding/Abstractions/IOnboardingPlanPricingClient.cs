using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>Puerto M2M Auth→Subscription para resolver el precio BRUTO del plan durante el onboarding
/// (pre-tenant). El bruto es autoritativo en Subscription (nunca del frontend); Auth lo necesita para
/// cotizar los códigos en Growth antes del checkout.</summary>
public interface IOnboardingPlanPricingClient
{
    Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(Guid planId, CancellationToken ct = default);
}

public sealed record OnboardingPlanPrice(long MonthlyPriceCents, string Currency);
