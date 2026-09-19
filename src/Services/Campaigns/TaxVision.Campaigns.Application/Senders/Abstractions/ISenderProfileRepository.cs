using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Application.Senders.Abstractions;

/// <summary>Repositorio del aggregate <see cref="SenderProfile"/> (aislamiento multitenant por repo).</summary>
public interface ISenderProfileRepository
{
    Task<SenderProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Carga varios remitentes por id (para resolver el <c>SenderRef</c> de un run). Solo los del tenant.</summary>
    Task<IReadOnlyList<SenderProfile>> GetManyByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<PagedResult<SenderProfile>> ListAsync(
        Guid tenantId,
        CampaignChannel? channel,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(SenderProfile sender, CancellationToken ct = default);
}
