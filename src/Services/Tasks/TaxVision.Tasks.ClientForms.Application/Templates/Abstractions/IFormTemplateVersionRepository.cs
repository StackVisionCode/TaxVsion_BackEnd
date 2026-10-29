using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

public interface IFormTemplateVersionRepository
{
    void Add(FormTemplateVersion version);
    Task<Result<FormTemplateVersion>> GetAsync(
        Guid tenantId,
        Guid templateId,
        int versionNumber,
        CancellationToken ct = default
    );
    Task<Result<FormTemplateVersion>> GetByIdAsync(Guid tenantId, Guid versionId, CancellationToken ct = default);
    Task<IReadOnlyList<FormTemplateVersion>> ListAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);
}
