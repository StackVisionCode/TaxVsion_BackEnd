using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.PaymentClient.Domain.Tenants;

/// <summary>
/// Proyección local del tenant, alimentada por <c>TenantCreatedIntegrationEvent</c> /
/// <c>TenantStatusChangedIntegrationEvent</c> (y el nombre comercial por
/// <c>TenantProfileChangedIntegrationEvent</c>). PaymentClient nunca llama a Auth/Tenant en el
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

    /// <summary>Sello <c>UpdatedAtUtc</c> de Tenant del último renombre / cambio de zona aplicado; null =
    /// nombre y zona del alta. Ordena los <c>TenantUpdatedIntegrationEvent</c>.</summary>
    public DateTime? SourceUpdatedAtUtc { get; private set; }

    /// <summary>Nombre comercial (DBA) del perfil de la oficina; null = sin dato.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Versión del perfil aplicado; 0 = nunca llegó uno. Ordena los
    /// <c>TenantProfileChangedIntegrationEvent</c>, independiente de <see cref="SourceUpdatedAtUtc"/>.</summary>
    public long ProfileVersion { get; private set; }

    /// <summary>Nombre de la oficina que ve el cliente pagador: el comercial si existe.</summary>
    public string CustomerFacingName => DisplayName ?? Name;

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

    /// <summary>La oficina renombró su subdominio primario (Auth es el dueño del dominio).</summary>
    public void ChangeSubDomain(string subDomain, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(subDomain))
            return;

        SubDomain = subDomain.Trim().ToLowerInvariant();
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Renombre / cambio de zona publicado por Tenant (estado completo). Ignora un evento más
    /// viejo que el aplicado; uno igual se reaplica (idempotente). Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyTenantUpdate(string name, string defaultTimeZoneId, DateTime sourceUpdatedAtUtc, DateTime nowUtc)
    {
        if (SourceUpdatedAtUtc is { } applied && sourceUpdatedAtUtc < applied)
            return false;
        if (string.IsNullOrWhiteSpace(name) || !IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalized))
            return false;

        Name = name.Trim();
        DefaultTimeZoneId = normalized;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Perfil publicado por Tenant (estado completo). Ignora una versión menor o igual a la
    /// aplicada (repetido o fuera de orden). Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyProfile(long version, string? displayName, DateTime nowUtc)
    {
        if (version <= ProfileVersion)
            return false;

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ProfileVersion = version;
        UpdatedAtUtc = nowUtc;
        return true;
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
