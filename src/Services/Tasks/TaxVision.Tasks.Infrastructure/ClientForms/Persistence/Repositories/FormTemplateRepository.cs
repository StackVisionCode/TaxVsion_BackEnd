using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormTemplateRepository(TasksDbContext context) : IFormTemplateRepository
{
    public void Add(FormTemplate template) => context.FormTemplates.Add(template);
    public void Remove(FormTemplate template) => context.FormTemplates.Remove(template);

    public async Task<Result<FormTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var template = await context.FormTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.TenantId == tenantId && item.Id == templateId,
            ct
        );
        return template is null ? Result.Failure<FormTemplate>(FormTemplateErrors.NotFound) : Result.Success(template);
    }

    public async Task<PagedResult<FormTemplate>> ListAsync(
        Guid tenantId,
        FormTemplateStatus? status,
        FormPurpose? purpose,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = context.FormTemplates.IgnoreQueryFilters().AsNoTracking().Where(item => item.TenantId == tenantId);
        if (status.HasValue)
            query = query.Where(item => item.Status == status.Value);
        if (purpose.HasValue)
            query = query.Where(item => item.Purpose == purpose.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(item => EF.Functions.Like(EF.Property<string>(item, "Name"), pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(item => EF.Property<string>(item, "Name")).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<FormTemplate>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AssignableFormTemplateRecord>> ListAssignableAsync(
        Guid tenantId,
        string? search,
        CancellationToken ct = default
    )
    {
        var templates = context.FormTemplates.IgnoreQueryFilters().AsNoTracking().Where(item =>
            item.TenantId == tenantId && item.Status == FormTemplateStatus.Active && item.LatestVersionId != null
        );
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            templates = templates.Where(item => EF.Functions.Like(EF.Property<string>(item, "Name"), pattern));
        }
        return await templates
            .Join(
                context.FormTemplateVersions.IgnoreQueryFilters().AsNoTracking(),
                template => template.LatestVersionId,
                version => (Guid?)version.Id,
                (template, version) => new AssignableFormTemplateRecord(template, version.Title)
            )
            .OrderBy(item => EF.Property<string>(item.Template, "Name"))
            .ToListAsync(ct);
    }
}
