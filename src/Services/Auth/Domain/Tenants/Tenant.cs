using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.Auth.Domain.Tenants;

public sealed class Tenant : BaseEntity
{
    private Tenant() { }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public TenantKind Kind { get; private set; }
    public string DefaultTimeZoneId { get; private set; } = default!;
    public bool IsActive { get; private set; }

    /// <summary>Acceso bloqueado por facturación (suscripción Suspended/Expired) — DISTINTO de
    /// <see cref="IsActive"/> (suspensión admin/seguridad). Lo mueve el ciclo de vida de la suscripción,
    /// no el admin de plataforma, para poder mostrarle al usuario el mensaje correcto ("renueva para
    /// recuperar acceso" vs "cuenta suspendida"). Los gates de login lo consultan.</summary>
    public bool BillingAccessBlocked { get; private set; }

    public string? BillingBlockReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Sello <c>UpdatedAtUtc</c> de Tenant del último renombre / cambio de zona aplicado. Null =
    /// nunca llegó un <c>TenantUpdatedIntegrationEvent</c> (nombre y zona son los del alta).</summary>
    public DateTime? SourceUpdatedAtUtc { get; private set; }

    /// <summary>Última versión de Tenant aplicada por <c>TenantStatusChangedIntegrationEvent</c>.</summary>
    public long? LastStatusVersion { get; private set; }

    /// <summary>Última versión de Tenant aplicada por <c>TenantUpdatedIntegrationEvent</c>. Va aparte de la de estado:
    /// la versión es una sola por oficina y cada aviso trae solo su parte, así que un renombre v6 no invalida un
    /// cambio de estado v5.</summary>
    public long? LastProfileVersion { get; private set; }

    public static Result<Tenant> Register(
        Guid id,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId
    )
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Tenant>(new Error("Tenant.Id", "Tenant id is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(new Error("Tenant.Name", "Tenant name is required."));
        }

        if (string.IsNullOrWhiteSpace(subDomain))
        {
            return Result.Failure<Tenant>(new Error("Tenant.SubDomain", "Tenant subdomain is required."));
        }

        if (!IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalizedTimeZoneId))
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.DefaultTimeZoneId", "Tenant default time zone must be a valid IANA identifier.")
            );
        }

        return Result.Success(
            new Tenant
            {
                Id = id,
                Name = name.Trim(),
                SubDomain = subDomain.Trim().ToLowerInvariant(),
                Kind = kind,
                DefaultTimeZoneId = normalizedTimeZoneId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
    }

    /// <summary>La oficina renombró su subdominio primario (el slug sale de ahí).</summary>
    public void ChangeSubDomain(string subDomain)
    {
        if (!string.IsNullOrWhiteSpace(subDomain))
            SubDomain = subDomain.Trim().ToLowerInvariant();
    }

    public void UpdateFromCreatedEvent(string name, string subDomain, TenantKind kind, string defaultTimeZoneId)
    {
        if (!IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalizedTimeZoneId))
        {
            throw new ArgumentException(
                "Tenant default time zone must be a valid IANA identifier.",
                nameof(defaultTimeZoneId)
            );
        }

        SubDomain = subDomain.Trim().ToLowerInvariant();
        Kind = kind;
        IsActive = true;

        // Un alta reentregada después de un renombre no pisa el nombre ni la zona más nuevos.
        if (SourceUpdatedAtUtc is not null)
            return;

        Name = name.Trim();
        DefaultTimeZoneId = normalizedTimeZoneId;
    }

    /// <summary>
    /// Renombre / cambio de zona publicado por Tenant (estado completo). Descarta un evento más viejo
    /// que el ya aplicado. Devuelve <c>true</c> si se aplicó.
    /// </summary>
    public bool ApplyTenantUpdate(
        string name,
        string defaultTimeZoneId,
        DateTime sourceUpdatedAtUtc,
        long? tenantVersion = null
    )
    {
        // Sin versión (mensaje viejo) ordena el reloj.
        if (tenantVersion is null && SourceUpdatedAtUtc is { } applied && sourceUpdatedAtUtc < applied)
            return false;

        if (tenantVersion <= LastProfileVersion)
            return false;

        if (string.IsNullOrWhiteSpace(name) || !IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalized))
            return false;

        Name = name.Trim();
        DefaultTimeZoneId = normalized;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        LastProfileVersion = tenantVersion ?? LastProfileVersion;
        return true;
    }

    /// <summary>Descarta un aviso de estado igual o más viejo que el aplicado. Sin versión (mensaje viejo) pasa.</summary>
    public bool AcceptStatusVersion(long? tenantVersion)
    {
        if (tenantVersion <= LastStatusVersion)
            return false;

        LastStatusVersion = tenantVersion ?? LastStatusVersion;
        return true;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Marca/limpia el bloqueo de acceso por facturación. Idempotente.</summary>
    public void SetBillingAccess(bool blocked, string? reason)
    {
        BillingAccessBlocked = blocked;
        BillingBlockReason = blocked ? reason : null;
    }
}
