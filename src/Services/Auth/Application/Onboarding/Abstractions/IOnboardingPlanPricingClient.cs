using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>Puerto M2M Auth→Subscription para resolver el precio BRUTO del plan durante el onboarding
/// (pre-tenant). El bruto es autoritativo en Subscription (nunca del frontend); Auth lo necesita para
/// cotizar los códigos en Growth antes del checkout.</summary>
public interface IOnboardingPlanPricingClient
{
    /// <summary>Bruto del plan para el CICLO elegido ("Monthly"/"Yearly"). El gross del código y el neto
    /// cobrado deben resolverse con el MISMO ciclo, o el guard net≤bruto de PaymentApp puede rechazar.</summary>
    Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(
        Guid planId,
        string billingCycle,
        CancellationToken ct = default
    );
}

public sealed record OnboardingPlanPrice(long GrossPriceCents, string Currency);
