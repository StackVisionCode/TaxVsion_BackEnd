using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Application.Abstractions;

public interface IDocumentValidationRepository
{
    Task AddAsync(DocumentValidationRecord record, CancellationToken ct = default);
}
