namespace TaxVision.Auth.Application.Onboarding;

/// <summary>PayFlow (Fase 9) — <see cref="RegistrationUrlBase"/> es el origen público que el
/// frontend sirve para completar el registro (<c>{RegistrationUrlBase}/register?token=...</c>).
/// Config real por ambiente; el valor de <c>appsettings.json</c> es solo un placeholder de
/// desarrollo.</summary>
public sealed class OnboardingOptions
{
    public const string SectionName = "Onboarding";

    public string RegistrationUrlBase { get; set; } = "http://localhost:5173";

    /// <summary>PayFlow (Fase 11) — origen público de Auth (el propio API), NO el frontend, usado
    /// para construir el link mediador de descarga del recibo
    /// (<c>{AuthPublicBaseUrl}/onboarding/receipts/{ReceiptFileId}/download</c>) embebido en el
    /// email de Notification (Fase 12). Config real por ambiente.</summary>
    public string AuthPublicBaseUrl { get; set; } = "http://localhost:5124";

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
