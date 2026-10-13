using BuildingBlocks.Platform;

namespace BuildingBlocks.Web.Platform;

/// <summary>
/// Sección <c>PlatformSettings</c>. Los valores de marca y dominio son SOLO el arranque: rigen hasta
/// que llega la versión de Tenant (y son con los que Tenant siembra su fila la primera vez). Una vez
/// sembrada, la fuente es Tenant, no esta config.
/// </summary>
public sealed class PlatformSettingsOptions
{
    public const string SectionName = "PlatformSettings";

    /// <summary>Base interna de Tenant (ej. <c>http://tenant-api:8080</c>). Vacía = solo valores locales.</summary>
    public string? TenantBaseUrl { get; set; }

    /// <summary>Cada cuánto se vuelve a leer de Tenant. El evento llega antes; esto cubre el que se pierda.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Espera entre reintentos mientras Tenant todavía no contestó ninguna vez.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);

    public string BrandName { get; set; } = "TaxOffix";
    public string LegalName { get; set; } = "TaxOffix LLC";
    public string BaseDomain { get; set; } = "taxoffix.io";
    public string ApiHost { get; set; } = "api.taxoffix.io";
    public string SupportEmail { get; set; } = "support@taxoffix.io";
    public string BillingEmail { get; set; } = "billing@taxoffix.io";

    /// <summary>Los valores locales como versión 0.</summary>
    public PlatformSettingsSnapshot ToBootstrapSnapshot() =>
        new(0, BrandName, LegalName, BaseDomain, ApiHost, SupportEmail, BillingEmail);
}
