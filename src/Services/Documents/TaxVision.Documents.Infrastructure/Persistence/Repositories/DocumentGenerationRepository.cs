using Microsoft.EntityFrameworkCore;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;

namespace TaxVision.Documents.Infrastructure.Persistence.Repositories;

/// <summary>
/// IgnoreQueryFilters() en todas las lecturas: los handlers/consumers corren en scopes de Wolverine
/// (bus.InvokeAsync, consumer de FileAvailable) sin ITenantContext ambiental — el filtro global
/// fail-closed devolvería 0 filas. El tenantId ya viene explícito y confiable del command/evento, así
/// que se filtra manualmente (guardrail: IgnoreQueryFilters solo con tenant validado por otra vía).
/// GetByFileIdAsync es cross-tenant deliberado (correlación del evento de CloudStorage); el llamador
/// valida el tenant contra la generación encontrada.
/// </summary>
public sealed class DocumentGenerationRepository(DocumentsDbContext dbContext) : IDocumentGenerationRepository
{
    public Task<DocumentGeneration?> GetByIdAsync(Guid tenantId, Guid generationId, CancellationToken ct = default) =>
        dbContext
            .DocumentGenerations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == generationId && g.TenantId == tenantId, ct);

    public Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) =>
        dbContext
            .DocumentGenerations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.IdempotencyKey == idempotencyKey, ct);

    public Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        dbContext.DocumentGenerations.IgnoreQueryFilters().FirstOrDefaultAsync(g => g.FileId == fileId, ct);

    public async Task AddAsync(DocumentGeneration generation, CancellationToken ct = default) =>
        await dbContext.DocumentGenerations.AddAsync(generation, ct);
}
