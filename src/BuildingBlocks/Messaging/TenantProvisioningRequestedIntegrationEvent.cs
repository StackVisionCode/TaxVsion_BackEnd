  namespace BuildingBlocks.Messaging;

/// <summary>
/// Publicado por Subscription Service cuando el pago del enrollment se confirma.
/// Tenant Service lo consume y crea el tenant, luego publica TenantCreatedIntegrationEvent
/// con el EnrollmentId incluido para que Subscription pueda activar la suscripción.
/// </summary>
public sealed record TenantProvisioningRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid EnrollmentId { get; init; }
    public required string PlanCode { get; init; }
    public required string AdminEmail { get; init; }
    public required string OrgName { get; init; }
    public required string Subdomain { get; init; }
    public required string TimeZoneId { get; init; }
}
