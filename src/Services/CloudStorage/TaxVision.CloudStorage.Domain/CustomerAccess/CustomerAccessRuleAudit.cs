using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.CustomerAccess;

/// <summary>
/// Audit fila-por-mutación de reglas deny: Created cada vez que un set entra o renueva una regla,
/// Removed solo cuando un clear efectivamente borra una. Reason es la snapshot del momento.
/// </summary>
public sealed class CustomerAccessRuleAudit : TenantEntity
{
    private CustomerAccessRuleAudit() { }

    public Guid CustomerId { get; private set; }
    public AccessResourceType ResourceType { get; private set; }
    public Guid ResourceId { get; private set; }
    public AccessCapability Capability { get; private set; }
    public AccessRuleAction Action { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }

    public static CustomerAccessRuleAudit Created(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string? reasonSnapshot
    ) =>
        Build(
            tenantId,
            customerId,
            resourceType,
            resourceId,
            capability,
            AccessRuleAction.Created,
            actorUserId,
            occurredAtUtc,
            reasonSnapshot
        );

    public static CustomerAccessRuleAudit Removed(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string? reasonSnapshot
    ) =>
        Build(
            tenantId,
            customerId,
            resourceType,
            resourceId,
            capability,
            AccessRuleAction.Removed,
            actorUserId,
            occurredAtUtc,
            reasonSnapshot
        );

    private static CustomerAccessRuleAudit Build(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        AccessRuleAction action,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string? reasonSnapshot
    )
    {
        var entry = new CustomerAccessRuleAudit
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Capability = capability,
            Action = action,
            ActorUserId = actorUserId,
            OccurredAtUtc = occurredAtUtc,
            Reason = reasonSnapshot,
        };
        entry.SetTenant(tenantId);
        return entry;
    }
}
