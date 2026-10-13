using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class SignatureTemplateRepository(SignatureDbContext db) : ISignatureTemplateRepository
{
    // Mismo bug de scope de Wolverine que SignatureRequestRepository.GetByIdAsync (ver
    // LocalCommandTenantMiddleware.cs): tenantId ya viene explícito y validado — IgnoreQueryFilters()
    // porque el filtro ambiental global puede no estar poblado en este scope de DI.
    public Task<SignatureTemplate?> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default) =>
        db
            .SignatureTemplates.IgnoreQueryFilters()
            .Include(t => t.Documents)
            .Include(t => t.Slots)
            .Include(t => t.Fields)
            .Include(t => t.PreparerFields)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct);

    public async Task AddAsync(SignatureTemplate template, CancellationToken ct = default) =>
        await db.SignatureTemplates.AddAsync(template, ct);

    public void Remove(SignatureTemplate template)
    {
        // Mismo caso que SignatureRequestRepository.Remove: TemplateField/TemplatePreparerField tienen
        // FK requerida a TemplateDocument con OnDelete(NoAction), así que al cascada-borrar los Documents
        // EF severa una relación requerida y lanza. Borramos esos fields primero. (Hoy las plantillas se
        // archivan, no se hard-borran, así que esto es defensivo para no dejar el footgun armado.)
        db.RemoveRange(template.Fields);
        db.RemoveRange(template.PreparerFields);
        db.SignatureTemplates.Remove(template);
    }
}
