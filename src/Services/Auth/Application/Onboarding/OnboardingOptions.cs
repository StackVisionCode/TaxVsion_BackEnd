namespace TaxVision.Auth.Application.Onboarding;

/// <summary>PayFlow (Fase 9) — <see cref="RegistrationUrlBase"/> es el origen público que el
/// frontend sirve para completar el registro (<c>{RegistrationUrlBase}/register?token=...</c>).
/// Config real por ambiente; el valor de <c>appsettings.json</c> es solo un placeholder de
/// desarrollo.</summary>
public sealed class OnboardingOptions
{
    public const string SectionName = "Onboarding";

    public string RegistrationUrlBase { get; set; } = "http://localhost:4200";

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

    /// <summary>TTL de la sesión corta emitida al verificar el OTP. Autoriza únicamente el carril
    /// de onboarding pre-tenant y no reemplaza el JWT normal de usuarios autenticados.</summary>
    public int OnboardingSessionTtlMinutes { get; set; } = 30;

    /// <summary>Minutos tras el pago liquidado antes de enviar el email "completa tu oficina", y
    /// SOLO si el onboarding sigue en RegistrationPending (quien termina in-session no lo recibe).
    /// El recibo y el carril $0 no se difieren. Lo evalúa el OnboardingRegistrationReminderScheduler.</summary>
    public int RegistrationReminderDelayMinutes { get; set; } = 45;

    /// <summary>TTL de la referencia Redis del raw token de registro cuando el email se difiere:
    /// debe cubrir la ventana del recordatorio más el intervalo del sweeper. El mismo token ya vive
    /// 72h en el buzón del comprador, así que una referencia acotada es MENOS exposición, no más. El
    /// carril inmediato ($0) sigue usando el TTL corto por defecto del store.</summary>
    public int RegistrationTokenReferenceTtlMinutes { get; set; } = 75;

    /// <summary>Máximo de reintentos de pago sobre el MISMO onboarding tras un fallo, antes de exigir
    /// empezar de cero. Lo aplica TenantOnboarding.ReopenForPaymentRetry.</summary>
    public int PaymentRetryMaxAttempts { get; set; } = 3;

    /// <summary>Ventana (desde la creación del onboarding) dentro de la cual se puede reintentar el
    /// pago. Fuera de ella, el comprador debe iniciar un onboarding nuevo. Alineada al token de 72h.</summary>
    public int PaymentRetryWindowHours { get; set; } = 72;

    /// <summary>TTL de la referencia de retorno opaca que viaja en el successUrl del checkout, para que
    /// el reconcile funcione al volver de Stripe aunque falte la cookie (otro navegador/incógnito).
    /// Cubre la ventana pago→retorno; corto a propósito (NO es credencial de registro, solo resuelve el
    /// onboardingId para consultar estado).</summary>
    public int OnboardingReturnReferenceTtlMinutes { get; set; } = 30;
}
