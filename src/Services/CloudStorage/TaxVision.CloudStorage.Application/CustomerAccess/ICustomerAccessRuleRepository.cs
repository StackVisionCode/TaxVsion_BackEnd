using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess;

public interface ICustomerAccessRuleRepository
{
    void Add(CustomerDocumentAccessRule rule);
    void Remove(CustomerDocumentAccessRule rule);

    /// <summary>Lookup por la clave única (customer, item, capability) — null si no hay deny declarado.</summary>
    Task<CustomerDocumentAccessRule?> GetAsync(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        CancellationToken ct
    );

    /// <summary>Todas las reglas de un cliente — paint del admin UI y payload del gate al leer.</summary>
    Task<IReadOnlyList<CustomerDocumentAccessRule>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct
    );
}

public interface ICustomerAccessRuleAuditRepository
{
    void Add(CustomerAccessRuleAudit entry);
}
