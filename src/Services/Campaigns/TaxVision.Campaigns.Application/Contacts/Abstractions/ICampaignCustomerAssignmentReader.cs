namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>
/// Lee el set de clientes ASIGNADOS a un usuario desde la proyección local de asignaciones (P2), para acotar
/// la audiencia "Clients" de una campaña cuando el actor no ve todos los clientes (customers.view_all). La
/// impl consulta la proyección compartida (kit BuildingBlocks.CustomerVisibility). Es distinto de
/// <see cref="ICustomerAudienceClient"/> (que trae el directorio completo de clientes activos por M2M).
/// </summary>
public interface ICampaignCustomerAssignmentReader
{
    Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
}
