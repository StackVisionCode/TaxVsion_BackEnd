using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

public interface IFormTemplateRepository
{
    void Add(FormTemplate template);
    void Remove(FormTemplate template);
    Task<Result<FormTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);
    Task<PagedResult<FormTemplate>> ListAsync(
        Guid tenantId,
        FormTemplateStatus? status,
        FormPurpose? purpose,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<AssignableFormTemplateRecord>> ListAssignableAsync(
        Guid tenantId,
        string? search,
        CancellationToken ct = default
    );
}

public sealed record AssignableFormTemplateRecord(FormTemplate Template, string Title);
