using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess;

/// <summary>Proyección de una regla deny para el admin UI y el consumer del gate.</summary>
public sealed record CustomerAccessRuleView(
    Guid Id,
    Guid CustomerId,
    AccessResourceType ResourceType,
    Guid ResourceId,
    AccessCapability Capability,
    Guid SetByUserId,
    DateTime SetAtUtc,
    string? Reason
);

public static class CustomerAccessRuleMapper
{
    public static CustomerAccessRuleView Map(CustomerDocumentAccessRule rule) =>
        new(
            rule.Id,
            rule.CustomerId,
            rule.ResourceType,
            rule.ResourceId,
            rule.Capability,
            rule.SetByUserId,
            rule.SetAtUtc,
            rule.Reason
        );
}
