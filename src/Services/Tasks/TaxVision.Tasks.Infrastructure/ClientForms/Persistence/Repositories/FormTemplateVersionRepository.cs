using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormTemplateVersionRepository(TasksDbContext context) : IFormTemplateVersionRepository
{
    public void Add(FormTemplateVersion version) => context.FormTemplateVersions.Add(version);

    public async Task<Result<FormTemplateVersion>> GetAsync(
        Guid tenantId,
        Guid templateId,
        int versionNumber,
        CancellationToken ct = default
    )
    {
        var version = await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == tenantId
                    && item.FormTemplateId == templateId
                    && item.VersionNumber == versionNumber,
                ct
            );
        return version is null
            ? Result.Failure<FormTemplateVersion>(FormTemplateVersionErrors.NotFound)
            : Result.Success(version);
    }

    public async Task<IReadOnlyList<FormTemplateVersion>> ListAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.FormTemplateId == templateId)
            .OrderByDescending(item => item.VersionNumber)
            .ToListAsync(ct);

    public async Task<Result<FormTemplateVersion>> GetByIdAsync(
        Guid tenantId,
        Guid versionId,
        CancellationToken ct = default
    )
    {
        var version = await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == versionId, ct);
        return version is null
            ? Result.Failure<FormTemplateVersion>(FormTemplateVersionErrors.NotFound)
            : Result.Success(version);
    }
}
