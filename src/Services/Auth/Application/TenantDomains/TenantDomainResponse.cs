using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains;

/// <summary>Fase A5 — vista admin de un TenantDomain, reutilizada por create/list.</summary>
public sealed record TenantDomainResponse(
    Guid Id,
    string DomainType,
    string Host,
    string Status,
    bool IsPrimary,
    string? VerificationMethod,
    DateTime? VerifiedAtUtc,
    DateTime CreatedAtUtc
)
{
    public static TenantDomainResponse From(TenantDomain domain) =>
        new(
            domain.Id,
            domain.DomainType.ToString(),
            domain.Host,
            domain.Status.ToString(),
            domain.IsPrimary,
            domain.VerificationMethod,
            domain.VerifiedAtUtc,
            domain.CreatedAtUtc
        );
}
