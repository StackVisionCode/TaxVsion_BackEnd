namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 13) — el comprador completó el form de registro: token canjeado, password
/// validado, subdomain reservado (formato+reservados, disponibilidad real es Fase 14),
/// TermsVersion vigente aceptada. Arranca la Saga de provisioning (Fase 15): crea Tenant, TenantAdmin,
/// Subscription, CloudStorage, Subdomain y defaults, en ese orden.
/// <para>
/// <see cref="IntegrationEvent.TenantId"/> se estampa con <c>BuildingBlocks.Tenancy.PlatformTenant.Id</c>
/// (NO <see cref="Guid.Empty"/>): el tenant real todavía no existe, pero
/// <c>BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware</c> (registrado globalmente para
/// todo <see cref="IIntegrationEvent"/> en Auth) rechaza con <see cref="InvalidOperationException"/>
/// cualquier evento con TenantId=Guid.Empty — el mismo sentinel que ya usan Documents Fase 10
/// (recibo pre-tenant) y el cliente M2M de Notification Fase 12 para "identidad de plataforma,
/// sin tenant real todavía". <see cref="OnboardingId"/> es la clave de correlación real de la Saga.
/// </para>
/// <para>
/// <see cref="PasswordHashReference"/> reusa el mismo mecanismo de un solo uso que
/// <c>ITokenReferenceStore</c>/<c>RedisTokenReferenceStore</c> ya implementan para el
/// RegistrationToken (Fase 9): el password NUNCA viaja en claro ni siquiera hasheado dentro de
/// este evento — solo una referencia Redis (GETDEL, TTL 30s) al hash PBKDF2 ya calculado por
/// <c>IPasswordHasher</c>. La Saga (Fase 15) debe canjearlo de inmediato al crear el TenantAdmin.
/// </para>
/// </summary>
public sealed record OnboardingProvisioningStartedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required Guid PlanId { get; init; }

    /// <summary>Ciclo de facturación elegido ("Monthly"/"Yearly"). Viaja hasta la activación de la
    /// suscripción para que nazca con el ciclo correcto. Default "Monthly" (compat con eventos en vuelo).</summary>
    public string BillingCycle { get; init; } = "Monthly";

    public required string OfficeName { get; init; }
    public required string RequestedSubdomain { get; init; }
    public required Guid TermsVersionId { get; init; }
    public required Guid PasswordHashReference { get; init; }

    /// <summary>Auditoría F17 — viaja hasta el comando de la Saga que crea el Tenant, y de ahí hasta
    /// Tenant, para que el guard de "onboarding listo" se resuelva localmente sin un M2M síncrono de
    /// vuelta a Auth.</summary>
    public required DateTime PaymentCompletedAtUtc { get; init; }
}
