using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>PayFlow (Fase 12) — M2M contra el endpoint one-shot de Auth
/// (<c>GET auth/internal/onboarding/tokens/{reference}/raw</c>) que resuelve un
/// <c>TokenReference</c> opaco a la URL de registro real (el raw token nunca viaja en los eventos
/// de integración — ver <c>OnboardingRegistrationReadyIntegrationEvent</c>).
/// <para>
/// Auditoría F15/F32 — ya NO es GETDEL de un solo uso: Auth resuelve la referencia con
/// <c>PeekAsync</c> (lectura no destructiva, respeta el TTL de 30s existente), justamente para que
/// un reintento de Wolverine tras un fallo transitorio encuentre el mismo raw token en vez de un
/// 404 por "ya consumido". Una referencia inexistente o expirada sigue devolviendo fallo.
/// </para></summary>
public interface IOnboardingTokenClient
{
    Task<Result<string>> ResolveRegistrationUrlAsync(Guid tokenReference, CancellationToken ct = default);
}
