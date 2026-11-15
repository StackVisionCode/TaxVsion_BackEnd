namespace BuildingBlocks.TenantTimeZones;

/// <summary>
/// Base URL directa a Tenant.Api (endpoint interno, no pasa por el Gateway; en Docker: http://tenant-api:8080)
/// y frecuencia del job. Cada servicio bindea la sección <c>TenantTimeZones:Reconciliation</c>.
/// </summary>
public sealed class TenantTimeZoneReconciliationOptions
{
    public const string SectionName = "TenantTimeZones:Reconciliation";

    /// <summary>Respaldo: la URL de Tenant que ya trae el compose a los servicios que heredan PlatformSettings.</summary>
    public const string PlatformTenantBaseUrlKey = "PlatformSettings:TenantBaseUrl";

    public const string LocalTenantBaseUrl = "http://localhost:5217";

    /// <summary>Vacío: se usa <see cref="PlatformTenantBaseUrlKey"/> y, si tampoco está, <see cref="LocalTenantBaseUrl"/>.</summary>
    public string TenantBaseUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Respaldo, no tiempo real: los eventos siguen siendo el camino principal.</summary>
    public int IntervalHours { get; set; } = 12;

    /// <summary>El directorio acepta hasta 200 por página.</summary>
    public int PageSize { get; set; } = 200;
}
