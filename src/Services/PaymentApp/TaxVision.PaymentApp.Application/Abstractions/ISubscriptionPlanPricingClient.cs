using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

public sealed record PlanMonthlyPrice(long AmountCents, string Currency);

/// <summary>PayFlow (Fase 16) — M2M hacia Subscription (<c>GET subscriptions/internal/plans/{planId}/pricing</c>)
/// para resolver el precio REAL de un plan server-side. Cierra el price-trust gap: antes de esto,
/// <c>CreateOnboardingCheckoutHandler</c> confiaba en el <c>PlanPriceCents</c>/<c>Currency</c> que el
/// caller (en última instancia, el frontend anónimo) enviaba sin validación alguna.</summary>
public interface ISubscriptionPlanPricingClient
{
    Task<Result<PlanMonthlyPrice>> GetMonthlyPriceAsync(Guid planId, CancellationToken ct = default);
}
