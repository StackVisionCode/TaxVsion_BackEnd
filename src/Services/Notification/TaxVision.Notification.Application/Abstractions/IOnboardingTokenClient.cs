using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>PayFlow (Fase 12) — M2M contra el endpoint one-shot de Auth
/// (<c>GET auth/internal/onboarding/tokens/{reference}/raw</c>) que resuelve un
/// <c>TokenReference</c> opaco a la URL de registro real (el raw token nunca viaja en los eventos
/// de integración — ver <c>OnboardingRegistrationReadyIntegrationEvent</c>). Single-use: una
/// segunda llamada con la misma referencia devuelve 404 (GETDEL atómico en Redis del lado de
/// Auth).</summary>
public interface IOnboardingTokenClient
{
    Task<Result<string>> ResolveRegistrationUrlAsync(Guid tokenReference, CancellationToken ct = default);
}
