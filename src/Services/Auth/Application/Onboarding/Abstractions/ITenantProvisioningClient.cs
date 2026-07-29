using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public sealed record CreateTenantForOnboardingRequest(
    Guid OnboardingId,
    string OfficeName,
    string Subdomain,
    string AdminEmail
);

/// <summary>PayFlow (Fase 15) — dispara la creación asíncrona del Tenant real
/// (<c>POST tenants/internal/from-onboarding</c>, Fase 16). No espera el <c>TenantId</c> en la
/// respuesta: <c>TenantCreatedForOnboardingIntegrationEvent</c> (publicado por Tenant) es la señal
/// real que la Saga espera. <see cref="Result"/> solo indica si la solicitud fue aceptada.</summary>
public interface ITenantProvisioningClient
{
    Task<Result> CreateTenantAsync(CreateTenantForOnboardingRequest request, CancellationToken ct = default);
}
