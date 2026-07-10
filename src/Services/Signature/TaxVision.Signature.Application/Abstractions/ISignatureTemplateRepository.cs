using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Abstractions;

public interface ISignatureTemplateRepository
{
    Task<SignatureTemplate?> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);

    Task AddAsync(SignatureTemplate template, CancellationToken ct = default);

    void Remove(SignatureTemplate template);
}
