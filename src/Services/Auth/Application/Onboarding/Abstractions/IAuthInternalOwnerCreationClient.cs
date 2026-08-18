using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public sealed record CreateTenantOwnerForOnboardingRequest(
    Guid OnboardingId,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid PasswordHashReference
);

/// <summary>PayFlow (Fase 15) — loopback HTTP hacia el propio Auth
/// (<c>POST internal/tenants/{tenantId}/owners</c>, Fase 16), no una llamada local de Wolverine:
/// el password nunca debe cruzar el bus de mensajería, así que el hash referenciado por
/// <see cref="CreateTenantOwnerForOnboardingRequest.PasswordHashReference"/> viaja únicamente por
/// este canal HTTP interno, canjeado del <c>ITokenReferenceStore</c> por el endpoint receptor.</summary>
public interface IAuthInternalOwnerCreationClient
{
    Task<Result> CreateOwnerAsync(CreateTenantOwnerForOnboardingRequest request, CancellationToken ct = default);
}
