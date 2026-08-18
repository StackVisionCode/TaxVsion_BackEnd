using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Plans.Queries;

public sealed record GetInternalPlanPricingQuery(Guid PlanId, BillingCycle BillingCycle);

public sealed record InternalPlanPricingResponse(Guid PlanId, long PriceCents, string Currency, string BillingCycle);

/// <summary>
/// PayFlow (Fase 16) — cierra el price-trust gap documentado en
/// <c>Auth.Application.Onboarding.TenantOnboardings.Commands.StartOnboardingCheckoutCommand</c>
/// (comentario "SECURITY GAP conocido y aceptado... DEBE cerrarse... en cuanto Fase 16 exista"):
/// PaymentApp consulta este endpoint M2M para resolver el precio REAL de un plan en vez de confiar
/// en el <c>PlanPriceCents</c>/<c>Currency</c> que el frontend enviaba antes sin validar. Precio
/// base (quantity 1) del tier Monthly de la versión publicada — mismo criterio que
/// <see cref="TaxVision.Subscription.Application.Common.PlanVersionEntitlements.GetPricesUsdByCycle"/>,
/// pero conservando la moneda (esa función solo devuelve el <c>decimal</c>) y convirtiendo a
/// centavos enteros porque PaymentApp's <c>Money</c> es <c>long AmountCents</c>, no <c>decimal</c>.
/// </summary>
public static class GetInternalPlanPricingHandler
{
    public static async Task<Result<InternalPlanPricingResponse>> Handle(
        GetInternalPlanPricingQuery query,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(query.PlanId, ct);
        var version = plan?.GetPublishedVersion();
        if (plan is null || version is null)
            return Result.Failure<InternalPlanPricingResponse>(
                new Error("Subscription.Plan.NotFound", "The plan is missing or unpublished.")
            );

        // Resuelve el precio base (quantity 1) del CICLO pedido (Monthly/Yearly/…), única fuente de verdad
        // del pricing. Comparte el helper con el job de renovación y la activación self-service.
        var price = PlanPricing.ResolveBaseSubscriptionPrice(version, query.BillingCycle);
        if (price is null)
            return Result.Failure<InternalPlanPricingResponse>(
                new Error("Subscription.Plan.NoPriceForCycle", $"The plan has no base {query.BillingCycle} price tier.")
            );

        return Result.Success(
            new InternalPlanPricingResponse(
                plan.Id,
                price.Value.AmountCents,
                price.Value.Currency,
                query.BillingCycle.ToString()
            )
        );
    }
}
