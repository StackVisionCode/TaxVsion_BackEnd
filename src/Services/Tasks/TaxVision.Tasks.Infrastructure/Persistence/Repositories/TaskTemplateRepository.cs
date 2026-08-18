using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Templates.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskTemplateRepository(TasksDbContext context) : ITaskTemplateRepository
{
    public void Add(TaskTemplate template) => context.TaskTemplates.Add(template);

    public async Task<Result<TaskTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var template = await context
            .TaskTemplates.IgnoreQueryFilters()
            .Include(t => t.Steps)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct);

        return template is null ? Result.Failure<TaskTemplate>(TaskErrors.Template.NotFound) : Result.Success(template);
    }

    public async Task<IReadOnlyList<TaskTemplate>> ListAsync(
        Guid tenantId,
        bool onlyActive,
        CancellationToken ct = default
    )
    {
        var query = context
            .TaskTemplates.IgnoreQueryFilters()
            .Include(t => t.Steps)
            .Include(t => t.Attachments)
            .Where(t => t.TenantId == tenantId);
        if (onlyActive)
            query = query.Where(t => t.IsActive);

        return await query.OrderBy(t => t.Name).ToListAsync(ct);
    }

    /// <summary>
    /// El cliente y el año viven dentro del owned <c>Reference</c>, así que se comparan en la
    /// proyección: el índice acota por plantilla y sobre esas pocas filas el filtro es trivial.
    /// </summary>
    public async Task<bool> WasAppliedAsync(
        Guid tenantId,
        Guid templateId,
        Guid? customerId,
        int? taxYear,
        CancellationToken ct = default
    ) =>
        await context
            .Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.TemplateId == templateId)
            .AnyAsync(t => t.Reference.CustomerId == customerId && t.Reference.TaxYear == taxYear, ct);
}
