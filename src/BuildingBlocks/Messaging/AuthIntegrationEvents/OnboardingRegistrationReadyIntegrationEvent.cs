namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 9) — el pago del onboarding se confirmó y el `RegistrationToken` ya existe
/// (hash persistido en `TenantOnboarding`). Notification (Fase 12) consume este evento, resuelve
/// el raw token vía el endpoint M2M one-shot de Auth (<c>GET /auth/internal/onboarding/tokens/{TokenReference}/raw</c>),
/// y renderiza el email — el raw NUNCA viaja en este evento (§3.6 del plan).
/// <see cref="IntegrationEvent.TenantId"/> queda en <c>Guid.Empty</c> (el tenant no existe
/// todavía) — <see cref="OnboardingId"/> es la clave de correlación real.
/// </summary>
public sealed record OnboardingRegistrationReadyIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid TokenReference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Deliberadamente nullable y sin poblar todavía — Auth no tiene acceso al
    /// catálogo de planes de Subscription hasta Fase 16. Scribe/Notification deben tolerar
    /// null (fallback genérico) hasta que se cierre ese M2M.</summary>
    public string? PlanName { get; init; }

    public required string PriceFormatted { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string RegistrationUrlBase { get; init; }
}
