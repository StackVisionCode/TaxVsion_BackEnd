using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess;

/// <summary>Proyección de una regla deny para el admin UI y el consumer del gate.</summary>
/// <remarks>
/// <c>ResourceName</c> es el nombre visible del folder/file al que la regla apunta — poblado
/// por el query handler desde los repos. Null cuando el recurso no tiene nombre real
/// (<see cref="AccessResourceType.TenantScope"/>) o cuando la fila referenciada ya no existe
/// (orphan tras un borrado posterior). El UI pinta un fallback "Deleted folder/file" en ese caso.
/// </remarks>
public sealed record CustomerAccessRuleView(
    Guid Id,
    Guid CustomerId,
    AccessResourceType ResourceType,
    Guid ResourceId,
    string? ResourceName,
    AccessCapability Capability,
    Guid SetByUserId,
    DateTime SetAtUtc,
    string? Reason
);

public static class CustomerAccessRuleMapper
{
    public static CustomerAccessRuleView Map(CustomerDocumentAccessRule rule, string? resourceName = null) =>
        new(
            rule.Id,
            rule.CustomerId,
            rule.ResourceType,
            rule.ResourceId,
            resourceName,
            rule.Capability,
            rule.SetByUserId,
            rule.SetAtUtc,
            rule.Reason
        );
}
