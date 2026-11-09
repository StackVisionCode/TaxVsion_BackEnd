using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Tests.Templates;

// Plantillas en memoria; nunca se aplicaron antes.
internal sealed class InMemoryTaskTemplateRepository(params TaskTemplate[] seed) : ITaskTemplateRepository
{
    private readonly List<TaskTemplate> _templates = [.. seed];

    public void Add(TaskTemplate template) => _templates.Add(template);

    public Task<Result<TaskTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var found = _templates.FirstOrDefault(t => t.TenantId == tenantId && t.Id == templateId);
        return Task.FromResult(
            found is null ? Result.Failure<TaskTemplate>(TaskErrors.Template.NotFound) : Result.Success(found)
        );
    }

    public Task<IReadOnlyList<TaskTemplate>> ListAsync(
        Guid tenantId,
        bool onlyActive,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<TaskTemplate>>(_templates.Where(t => t.TenantId == tenantId).ToList());

    public Task<bool> WasAppliedAsync(
        Guid tenantId,
        Guid templateId,
        Guid? customerId,
        int? taxYear,
        CancellationToken ct = default
    ) => Task.FromResult(false);
}
