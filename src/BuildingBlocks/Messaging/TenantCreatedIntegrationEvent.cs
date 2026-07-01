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
    public DateTime? AdminInvitationExpiresAtUtc { get; init; }
    /// <summary>
    /// Presente solo cuando el tenant fue creado como resultado de un SubscriptionEnrollment.
    /// Null = creación manual por Platform Admin.
    /// </summary>
    public Guid? EnrollmentId { get; init; }
}
