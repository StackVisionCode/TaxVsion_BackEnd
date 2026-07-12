using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class SignatureTemplateRepository(SignatureDbContext db) : ISignatureTemplateRepository
{
    public Task<SignatureTemplate?> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default) =>
        db
            .SignatureTemplates.Include(t => t.Slots)
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, ct);

    public async Task AddAsync(SignatureTemplate template, CancellationToken ct = default) =>
        await db.SignatureTemplates.AddAsync(template, ct);

    public void Remove(SignatureTemplate template) => db.SignatureTemplates.Remove(template);
}
