using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Settings;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormModuleSettingsRepository(TasksDbContext context) : IFormModuleSettingsRepository
{
    public Task<FormModuleSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        context
            .FormModuleSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<FormModuleSettings>> ListAsync(CancellationToken ct = default) =>
        await context
            .FormModuleSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(settings => settings.TenantId)
            .ToArrayAsync(ct);

    public void Add(FormModuleSettings settings) => context.FormModuleSettings.Add(settings);
}
