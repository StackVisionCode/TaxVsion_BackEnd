using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — adaptador hacia el contrato compartido de BuildingBlocks
/// (<see cref="BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer"/>), consumido por
/// <c>HttpPlanRateLimitReader</c> para llamar el catálogo M2M de Subscription
/// (GET internal/plan-rate-limits).
///
/// A diferencia de Tenant/Customer (un solo cliente de servicio), Billing ya tenía
/// <see cref="IServiceTokenProvider"/> — un proveedor multi-cliente nombrado (Documents, Payments,
/// …), cada uno con sus propias credenciales/scope en Auth (punto 10 del review original, ver
/// ServiceTokenProvider). Su firma (clientName + tenantId) no calza con el contrato de un solo
/// parámetro de IServiceTokenAcquirer, así que este adaptador fija el nombre de cliente
/// "Subscription" (bajo Billing:ServiceClients:Clients) en vez de reimplementar el cacheo M2M.
/// </summary>
internal sealed class SubscriptionServiceTokenAcquirer(IServiceTokenProvider tokenProvider)
    : BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer
{
    private const string ClientName = "Subscription";

    public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
        tokenProvider.GetTokenAsync(ClientName, tenantId, ct);
}
