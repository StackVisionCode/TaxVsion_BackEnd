using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

public sealed record PlanPrice(long AmountCents, string Currency);

/// <summary>PayFlow (Fase 16) — M2M hacia Subscription (<c>GET internal/plans/{planId}/pricing?cycle=</c>)
/// para resolver el precio REAL de un plan server-side, para el CICLO elegido ("Monthly"/"Yearly").
/// Cierra el price-trust gap: antes de esto, <c>CreateOnboardingCheckoutHandler</c> confiaba en el
/// <c>PlanPriceCents</c>/<c>Currency</c> que el caller (frontend anónimo) enviaba sin validación.</summary>
public interface ISubscriptionPlanPricingClient
{
    Task<Result<PlanPrice>> GetPriceAsync(Guid planId, string billingCycle, CancellationToken ct = default);
}
