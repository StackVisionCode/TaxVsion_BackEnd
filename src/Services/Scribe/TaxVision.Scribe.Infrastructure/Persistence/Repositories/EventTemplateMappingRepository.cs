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

    // IgnoreQueryFilters: llamado desde GetEventTemplateMappingsHandler vía bus.InvokeAsync (patrón
    // CQRS-vía-Wolverine) — el TenantContext ambiente puede llegar vacío al scope de DI del handler
    // aunque el middleware HTTP ya resolvió el tenant correctamente en el scope de la request. El
    // parámetro tenantId explícito (validado desde el JWT en el controller) ya garantiza el
    // aislamiento, así que el filtro ambiental roto no puede seguir ANDeando 0 filas (RBAC Fase 5).
    public async Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default) =>
        await dbContext
            .EventTemplateMappings.IgnoreQueryFilters()
            .Where(m => m.Scope == TemplateScope.System || m.TenantId == tenantId)
            // Ordenar por el VO entero (no por .Value) — EF traduce la propiedad convertida
            // directo a la columna, pero no siempre logra bajar el acceso a .Value dentro de OrderBy.
            .OrderBy(m => m.EventKey)
            .ToListAsync(ct);

    // IgnoreQueryFilters: llamado exclusivamente desde el pipeline de render M2M
    // (EventTemplateResolver ← RenderController, ActorType.Service — sin claim tenant_id, así que
    // el ITenantContext ambiente nunca refleja el tenant real del render). El aislamiento acá lo da
    // el parámetro tenantId explícito de la query, no el filtro global (RBAC Fase 5).
    public async Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
        EventKey eventKey,
        Guid? tenantId,
        CancellationToken ct = default
    ) =>
        await dbContext
            .EventTemplateMappings.IgnoreQueryFilters()
            .Where(m =>
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
