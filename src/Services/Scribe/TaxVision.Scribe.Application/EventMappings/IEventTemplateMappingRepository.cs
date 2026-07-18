using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.EventMappings;

public interface IEventTemplateMappingRepository
{
    Task AddAsync(EventTemplateMapping mapping, CancellationToken ct = default);

    Task<Result<EventTemplateMapping>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// Candidatos habilitados para un evento, visibles desde el contexto de <paramref name="tenantId"/>
    /// (System siempre + Tenant propio si aplica). El resolver elige entre ellos por prioridad de scope/locale.
    /// </summary>
    Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
        EventKey eventKey,
        Guid? tenantId,
        CancellationToken ct = default
    );

    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);
}
