namespace TaxVision.Sms.Application.Abstractions;

/// <summary>
/// Set de clientes ASIGNADOS a un usuario, leído de la proyección local de asignaciones (P2). Acota a
/// quién le puede escribir un preparador: el lote se resuelve con una sola consulta, no una por mensaje.
/// La impl delega en el kit compartido <c>BuildingBlocks.CustomerVisibility</c>, igual que Campaigns.
/// </summary>
public interface ISmsCustomerAssignmentReader
{
    Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
}
