using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

/// <summary>Fase 18 (Credentials Hardening) — M2M hacia Auth
/// (<c>POST internal/invitations/token-references</c>): deposita el raw token de activación
/// del TenantAdmin antes de publicar <c>TenantCreatedIntegrationEvent</c>, para que el evento solo
/// lleve una referencia (Guid, one-shot, TTL 30s) y nunca el token en claro por RabbitMQ — mismo
/// patrón TokenReference que Onboarding (Fase 9).</summary>
public interface IAuthInvitationTokenReferenceClient
{
    Task<Result<Guid>> StoreAsync(string rawToken, CancellationToken ct = default);
}
