using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

public sealed record OnboardingStatusSnapshot(string Status, DateTime? PaymentCompletedAtUtc);

/// <summary>PayFlow (Fase 16) — M2M hacia Auth (<c>GET auth/internal/onboarding/{onboardingId}/status</c>),
/// consultado por <c>CreateTenantFromOnboardingHandler</c> antes de crear un Tenant real: defensa en
/// profundidad para que solo onboardings genuinamente en <c>Provisioning</c> con pago confirmado
/// puedan disparar la creación de un tenant, sin depender únicamente de la policy ServiceOnly.</summary>
public interface IAuthOnboardingStatusClient
{
    Task<Result<OnboardingStatusSnapshot>> GetStatusAsync(Guid onboardingId, CancellationToken ct = default);
}
