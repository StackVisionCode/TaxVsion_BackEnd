using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.PaymentClient.Domain.Tenants;

/// <summary>
/// Proyección local del tenant, alimentada por <c>TenantCreatedIntegrationEvent</c> /
/// <c>TenantStatusChangedIntegrationEvent</c>. PaymentClient nunca llama a Auth/Tenant en el
/// hot path de un cobro (§42.2 del diseño) — misma estrategia que PaymentApp.
/// </summary>
public sealed class Tenant : BaseEntity
{
    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public TenantKind Kind { get; private set; }
    public string DefaultTimeZoneId { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Tenant() { }

    public static Result<Tenant> Register(
        Guid id,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        DateTime nowUtc
    )
    {
        if (id == Guid.Empty)
            return Result.Failure<Tenant>(new Error("Tenant.InvalidId", "Tenant id is required."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tenant>(new Error("Tenant.InvalidName", "Tenant name is required."));

        if (string.IsNullOrWhiteSpace(subDomain))
            return Result.Failure<Tenant>(new Error("Tenant.InvalidSubDomain", "Tenant subdomain is required."));

        if (!IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalizedTimeZoneId))
            return Result.Failure<Tenant>(
                new Error("Tenant.InvalidTimeZone", "Tenant default time zone must be a valid IANA identifier.")
            );

        return Result.Success(
            new Tenant
            {
                Id = id,
                Name = name.Trim(),
                SubDomain = subDomain.Trim().ToLowerInvariant(),
                Kind = kind,
                DefaultTimeZoneId = normalizedTimeZoneId,
                Status = "Active",
                IsActive = true,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            }
        );
    }

    public void ApplyStatusChange(string status, bool isActive, DateTime nowUtc)
    {
        Status = status;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>El Platform tenant nunca es sujeto de cobro — solo un tenant Customer activo
    /// puede pasar el gate de <c>TenantStatusGateMiddleware</c>.</summary>
    public bool CanOperate() => IsActive && Kind == TenantKind.Customer;
}
