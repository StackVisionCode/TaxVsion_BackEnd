using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class EventTemplateMappingRepository(ScribeDbContext dbContext) : IEventTemplateMappingRepository
{
    public async Task AddAsync(EventTemplateMapping mapping, CancellationToken ct = default) =>
        await dbContext.EventTemplateMappings.AddAsync(mapping, ct);

    public async Task<Result<EventTemplateMapping>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var mapping = await dbContext.EventTemplateMappings.FirstOrDefaultAsync(m => m.Id == id, ct);
        return mapping is null
            ? Result.Failure<EventTemplateMapping>(
                new Error("EventTemplateMapping.NotFound", $"Event template mapping {id} was not found.")
            )
            : Result.Success(mapping);
    }

    public async Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default) =>
        await dbContext
            .EventTemplateMappings.Where(m => m.Scope == TemplateScope.System || m.TenantId == tenantId)
            // Ordenar por el VO entero (no por .Value) — EF traduce la propiedad convertida
            // directo a la columna, pero no siempre logra bajar el acceso a .Value dentro de OrderBy.
            .OrderBy(m => m.EventKey)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
        EventKey eventKey,
        Guid? tenantId,
        CancellationToken ct = default
    ) =>
        await dbContext
            .EventTemplateMappings.Where(m =>
                m.EventKey == eventKey
                && m.Enabled
                && (m.Scope == TemplateScope.System || (m.Scope == TemplateScope.Tenant && m.TenantId == tenantId))
            )
            .ToListAsync(ct);

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var mapping = await dbContext.EventTemplateMappings.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mapping is null)
            return false;

        dbContext.EventTemplateMappings.Remove(mapping);
        return true;
    }
}
