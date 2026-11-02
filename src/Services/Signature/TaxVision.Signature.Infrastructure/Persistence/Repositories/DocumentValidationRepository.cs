using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class DocumentValidationRepository(SignatureDbContext db) : IDocumentValidationRepository
{
    public async Task AddAsync(DocumentValidationRecord record, CancellationToken ct = default) =>
        await db.DocumentValidationRecords.AddAsync(record, ct);

    // Sin filtro de tenant del contexto: lo llaman consumers sin request HTTP; el tenant va explícito.
    public Task<DocumentValidationRecord?> FindInspectedAsync(
        Guid tenantId,
        string contentSha256,
        CancellationToken ct = default
    )
    {
        var sha = contentSha256.Trim().ToLowerInvariant();
        return db
            .DocumentValidationRecords.IgnoreQueryFilters()
            .Where(record => record.TenantId == tenantId && record.ContentSha256 == sha && record.PageCount != null)
            .OrderByDescending(record => record.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }
}
