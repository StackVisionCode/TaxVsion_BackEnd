namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Cliente M2M read-only hacia <c>GET customers/internal/reconciliation</c> (Customer.Api, cross-tenant,
/// solo token de PlatformTenant). Lo consume <c>CustomerProjectionReconciliationJob</c> para auto-reparar
/// la proyección <c>CustomerEmailAddress</c> cuando se pierden eventos o el servicio nació después de
/// que ya existían customers. Nunca lanza: devuelve null en cualquier fallo de token/HTTP y el job decide.
///
/// <para>
/// Distinto de <see cref="ICorrespondenceCustomerClient"/> (que pagina <c>customers/internal/list</c> por
/// tenant y solo ve customers activos): este endpoint es global (todos los tenants, un solo token de
/// PlatformTenant) y devuelve también inactivos/archivados, así que además detecta el drift inverso
/// (customer desactivado en origen pero aún activo en la proyección local) que el otro camino no puede.
/// </para>
/// </summary>
public interface ICustomerReconciliationClient
{
    Task<CustomerReconciliationPage?> ListPageAsync(int page, int size, CancellationToken ct = default);
}

/// <summary>Una página del stream global de reconciliación de customers.</summary>
public sealed record CustomerReconciliationPage(IReadOnlyList<RemoteCustomerRecord> Items, bool HasMore);

/// <summary>
/// Fila cross-tenant de la fuente autoritativa. <see cref="IsActive"/> es <c>true</c> solo cuando el
/// customer está <c>Active</c> en Customer (los <c>Inactive</c>/<c>Archived</c> se reflejan como
/// soft-delete en la proyección local).
/// </summary>
public sealed record RemoteCustomerRecord(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    string PrimaryEmail,
    bool IsActive
);
