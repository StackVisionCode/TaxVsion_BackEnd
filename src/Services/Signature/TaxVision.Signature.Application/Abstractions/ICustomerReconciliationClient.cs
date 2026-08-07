namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Cliente M2M read-only hacia <c>GET internal/customers/reconciliation</c> (Customer.Api, cross-tenant,
/// solo token de PlatformTenant). Lo consume <c>CustomerProjectionReconciliationJob</c> para auto-reparar
/// la proyección <c>CustomerEmailProjection</c> cuando se pierden eventos o el servicio nació después de
/// que ya existían customers. Nunca lanza: devuelve null en cualquier fallo de token/HTTP y el job decide.
/// </summary>
public interface ICustomerReconciliationClient
{
    Task<CustomerReconciliationPage?> ListPageAsync(int page, int size, CancellationToken ct = default);
}

/// <summary>Una página del stream global de reconciliación de customers.</summary>
public sealed record CustomerReconciliationPage(IReadOnlyList<RemoteCustomerRecord> Items, bool HasMore);

/// <summary>
/// Fila cross-tenant de la fuente autoritativa. <see cref="IsActive"/> es <c>true</c> solo cuando el
/// customer está <c>Active</c> en Customer (los <c>Inactive</c>/<c>Archived</c> se reflejan como archivados
/// en la proyección local).
/// </summary>
public sealed record RemoteCustomerRecord(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    string PrimaryEmail,
    bool IsActive
);
