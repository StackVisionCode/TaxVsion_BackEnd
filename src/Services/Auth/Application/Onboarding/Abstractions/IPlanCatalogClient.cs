using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// PayFlow — resuelve el nombre de un plan desde el catálogo público de Subscription
/// (<c>GET plans</c>, anónimo). Cierra el gap documentado desde Fase 9 ("PlanName real no
/// disponible en Auth hasta Fase 16") que nunca se cerró realmente: el email de registro
/// mostraba literalmente "para el plan tu plan" (fallback del consumer sobre un PlanName
/// siempre null), el recibo generado tenía "Selected Plan" hardcodeado, y el preview de
/// registro devolvía PlanName null.
/// </summary>
public interface IPlanCatalogClient
{
    /// <summary>Best-effort: null si el plan no existe en el catálogo o si Subscription no
    /// responde — nunca falla el flujo de onboarding por esto, es solo un dato de display.</summary>
    Task<string?> GetPlanNameAsync(Guid planId, CancellationToken ct = default);
}
