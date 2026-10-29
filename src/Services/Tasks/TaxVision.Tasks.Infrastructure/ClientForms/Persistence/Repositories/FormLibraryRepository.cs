using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

// Única lectura entre tenants del módulo: siempre filtra TenantId == PlatformTenant.Id (D-17).
// Expone la última versión publicada: la revisión experta ya no es requisito (decisión del dueño, 2026-10-28).
internal sealed class FormLibraryRepository(TasksDbContext context) : IFormLibraryRepository
{
    public async Task<IReadOnlyList<LibraryTemplateRecord>> ListAsync(CancellationToken ct = default)
    {
        var platformId = PlatformTenant.Id;
        var templates = await context
            .FormTemplates.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == platformId && item.IsLibraryListed && item.LatestVersionNumber > 0)
            .Select(item => new
            {
                item.LibraryKey,
                Name = (string)(object)item.Name,
                item.Description,
                item.Purpose,
                item.LatestVersionNumber,
            })
            .ToListAsync(ct);

        return
        [
            .. templates.Select(item => new LibraryTemplateRecord(
                item.LibraryKey!,
                item.Name,
                item.Description?.Value,
                item.Purpose,
                item.LatestVersionNumber
            )),
        ];
    }

    public async Task<LibraryTemplateDetailRecord?> GetByKeyAsync(string libraryKey, CancellationToken ct = default)
    {
        var platformId = PlatformTenant.Id;
        var key = libraryKey.Trim().ToLowerInvariant();
        var template = await context
            .FormTemplates.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == platformId
                    && item.IsLibraryListed
                    && item.LatestVersionNumber > 0
                    && item.LibraryKey == key,
                ct
            );
        if (template is null)
            return null;

        var version = await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == platformId
                    && item.FormTemplateId == template.Id
                    && item.VersionNumber == template.LatestVersionNumber,
                ct
            );
        if (version is null)
            return null;

        return new LibraryTemplateDetailRecord(
            template.Id,
            template.LibraryKey!,
            template.Name.Value,
            template.Description?.Value,
            template.Purpose,
            template.LatestVersionNumber,
            version.Definition.Json
        );
    }
}
