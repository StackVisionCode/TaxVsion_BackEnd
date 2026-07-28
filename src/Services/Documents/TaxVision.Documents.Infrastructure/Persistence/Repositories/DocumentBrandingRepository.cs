using Microsoft.EntityFrameworkCore;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Infrastructure.Persistence.Repositories;

/// <summary>
/// IgnoreQueryFilters() + tenantId explícito: la lectura del branding se hace dentro del scope de
/// Wolverine del render (ProcessInvoiceGeneration), sin ITenantContext ambiental — el filtro global
/// fail-closed devolvería 0 filas. El tenantId viene explícito y confiable del comando/evento.
/// </summary>
public sealed class DocumentBrandingRepository(DocumentsDbContext dbContext) : IDocumentBrandingRepository
{
    public Task<DocumentBranding?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.DocumentBrandings.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);

    public async Task AddAsync(DocumentBranding branding, CancellationToken ct = default) =>
        await dbContext.DocumentBrandings.AddAsync(branding, ct);
}
