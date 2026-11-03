using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.PaymentApp.Domain.Tenants;

/// <summary>
/// Proyección local (read-only desde el punto de vista de negocio) del tenant, alimentada
/// por <c>TenantCreatedIntegrationEvent</c> / <c>TenantStatusChangedIntegrationEvent</c>.
/// PaymentApp NUNCA llama al servicio Auth/Tenant en el hot path de un cobro — confía en
/// esta copia local. Es la misma estrategia que usa Auth con su propio <c>TenantRegistry</c>
/// (guardrail de aislamiento §42.2 del diseño). <see cref="Status"/> se conserva crudo
/// (no solo <see cref="IsActive"/>) porque <c>TenantStatusGateMiddleware</c> necesita
/// distinguir <c>Suspended</c> de <c>Closed</c>, no solo activo/inactivo.
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

    public const int BillingLegalNameMaxLength = 200;
    public const int BillingEinMaxLength = 20;
    public const int BillingAddressMaxLength = 1000;

    /// <summary>"Billed to" del recibo SaaS (perfil de Tenant). Null = sin dato: el recibo muestra el nombre.</summary>
    public string? BillingLegalName { get; private set; }
    public string? BillingEin { get; private set; }

    /// <summary>Dirección postal en una línea.</summary>
    public string? BillingAddress { get; private set; }

    /// <summary>Última versión del perfil aplicada; 0 = nunca llegó. Independiente de <see cref="SourceUpdatedAtUtc"/>.</summary>
    public long ProfileVersion { get; private set; }

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

    /// <summary>Perfil de la oficina publicado por Tenant (estado completo). Solo aplica una versión mayor a
    /// la guardada (igual o menor se ignora: idempotente). Vacíos → null. Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyProfile(long version, string? legalName, string? ein, string? address, DateTime nowUtc)
    {
        if (version <= ProfileVersion)
            return false;

        BillingLegalName = Normalize(legalName);
        BillingEin = Normalize(ein);
        BillingAddress = Normalize(address);
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

    /// <summary>El Platform tenant (TaxVision interno) nunca es sujeto de cobro — solo un
    /// tenant Customer activo puede pasar el gate de <c>TenantStatusGateMiddleware</c>.</summary>
    public bool CanOperate() => IsActive && Kind == TenantKind.Customer;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
