using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>PayFlow (Fase 14) — Auth le pregunta a Tenant si un slug ya está en uso por un tenant
/// real (<c>GET internal/tenants/subdomain-available?slug=</c>, ServiceOnly). Complementa el
/// chequeo local de <see cref="IOnboardingSubdomainReservationRepository"/> (reservas temporales
/// dentro de Auth) — juntos cubren "¿ya existe?" y "¿alguien más lo está reservando ahora mismo?".</summary>
public interface ITenantSubdomainAvailabilityClient
{
    Task<Result<bool>> IsTakenAsync(string slug, CancellationToken ct = default);
}
