namespace TaxVision.Auth.Application.Onboarding;

/// <summary>PayFlow (Fase 9) — <see cref="RegistrationUrlBase"/> es el origen público que el
/// frontend sirve para completar el registro (<c>{RegistrationUrlBase}/register?token=...</c>).
/// Config real por ambiente; el valor de <c>appsettings.json</c> es solo un placeholder de
/// desarrollo.</summary>
public sealed class OnboardingOptions
{
    public const string SectionName = "Onboarding";

    public string RegistrationUrlBase { get; set; } = "http://localhost:5173";

    /// <summary>Origen INTERNO de Auth: loopback de la saga (creación del owner vía
    /// <c>internal/tenants/{id}/owners</c>, que NO pasa por el Gateway). En prod es
    /// <c>http://auth-api:8080</c>. Para el link PÚBLICO del recibo del email, ver
    /// <see cref="ReceiptDownloadBaseUrl"/> — no reusar este.</summary>
    public string AuthPublicBaseUrl { get; set; } = "http://localhost:5124";

    /// <summary>Origen PÚBLICO de Auth (vía Gateway, <c>api.taxproffice.com</c>) para el link
    /// mediador de descarga del recibo embebido en el email
    /// (<c>{ReceiptDownloadBaseUrl}/onboarding/receipts/{ReceiptFileId}/download</c>). Distinto de
    /// <see cref="AuthPublicBaseUrl"/> (loopback interno). Config real por ambiente.</summary>
    public string ReceiptDownloadBaseUrl { get; set; } = "http://localhost:5124";

    /// <summary>PayFlow (Fase 13) — dominio base para componer el link de redirect
    /// (<c>https://{RequestedSubdomain}.{TenantBaseDomain}</c>) que GetOnboardingStatusHandler
    /// expone una vez el onboarding llega a Completed. Copia deliberada de
    /// <c>TenantDomainOptions.BaseDomain</c> (mismo valor de config en la práctica) — el módulo
    /// Onboarding no puede depender del módulo TenantDomains (fitness function
    /// OnboardingModuleArchitectureTests), así que este valor se configura una segunda vez acá en
    /// vez de referenciar esa clase.</summary>
    public string TenantBaseDomain { get; set; } = "taxproffice.com";

    /// <summary>PayFlow (Fase 14) — TTL de la reserva temporal de subdominio durante el registro
    /// post-pago (60min por objetivo del plan, distinto del TTL de 15min que usa
    /// <c>TenantDomainOptions.SubdomainReservationTtlMinutes</c> para el flujo de PlatformAdmin —
    /// son módulos y flujos separados a propósito).</summary>
    public int SubdomainReservationTtlMinutes { get; set; } = 60;
}
