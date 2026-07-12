using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class DocumentValidationRepository(SignatureDbContext db) : IDocumentValidationRepository
{
    public async Task AddAsync(DocumentValidationRecord record, CancellationToken ct = default) =>
        await db.DocumentValidationRecords.AddAsync(record, ct);
}
