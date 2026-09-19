using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns.Abstractions;

/// <summary>
/// Repositorio del aggregate root <see cref="Campaign"/>. Toda lectura por tenant filtra por
/// <c>TenantId</c> explícito (aislamiento multitenant a nivel de repo).
/// </summary>
public interface ICampaignRepository
{
    /// <summary>Busca por <c>(TenantId, Id)</c>. Devuelve <c>null</c> si no existe o es de otro tenant.</summary>
    Task<Campaign?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Lista de campañas del tenant (más recientes primero), opcionalmente filtrada por estado.</summary>
    Task<PagedResult<Campaign>> ListAsync(
        Guid tenantId,
        CampaignStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(Campaign campaign, CancellationToken ct = default);

    /// <summary>Elimina la campaña (sus selecciones de remitente caen por cascade). Los runs históricos quedan.</summary>
    void Remove(Campaign campaign);
}
