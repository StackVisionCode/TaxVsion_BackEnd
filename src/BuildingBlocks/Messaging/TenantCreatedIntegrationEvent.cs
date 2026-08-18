namespace BuildingBlocks.Messaging;

public sealed record TenantCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid NewTenantId { get; init; }
    public required string Name { get; init; }
    public required string SubDomain { get; init; }
    public string Kind { get; init; } = Tenancy.TenantKind.Customer.ToString();
    public string DefaultTimeZoneId { get; init; } = TimeZones.IanaTimeZone.UtcId;
    public required string AdminEmail { get; init; }
    public required string AdminInvitationTokenHash { get; init; }

    /// <summary>Fase 18 — reemplaza el AdminInvitationRawToken (string) que antes viajaba en claro
    /// por RabbitMQ: el emisor (Tenant) deposita el raw token en Auth vía
    /// POST internal/invitations/token-references ANTES de publicar este evento, y solo la
    /// referencia (Guid, one-shot, TTL 30s) cruza el bus — mismo patrón TokenReference que
    /// Onboarding (Fase 9). Null en el flujo de PayFlow (onboarding pago-primero, ver
    /// OnboardingId), donde no se crea Invitation.</summary>
    public Guid? AdminInvitationTokenReference { get; init; }
    public DateTime? AdminInvitationExpiresAtUtc { get; init; }

    /// <summary>PayFlow (Fase 16) — presente solo cuando el tenant se creó vía el flujo de
    /// onboarding pagado (<c>POST internal/tenants/from-onboarding</c>). Los consumers de
    /// Subscription y Auth lo usan para saltar su trabajo normal (trial automático / Invitation de
    /// TenantAdmin), porque la Saga (Fase 15) ya se encarga de esos dos pasos por su cuenta.</summary>
    public Guid? OnboardingId { get; init; }
}
