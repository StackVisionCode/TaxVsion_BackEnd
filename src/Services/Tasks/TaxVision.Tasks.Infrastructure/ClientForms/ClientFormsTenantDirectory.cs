using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

// Una fila por tenant: se filtra en memoria con la misma regla que el evaluador.
internal sealed class ClientFormsTenantDirectory(TasksDbContext context) : IClientFormsTenantDirectory
{
    public async Task<IReadOnlySet<Guid>> ListAvailableTenantIdsAsync(CancellationToken ct = default)
    {
        var projections = await context.TenantPlanCodeProjections.IgnoreQueryFilters().AsNoTracking().ToArrayAsync(ct);
        return projections
            .Where(item =>
                item.EnabledModules.Contains(ClientFormsModule.RequiredModule, StringComparer.OrdinalIgnoreCase)
            )
            .Select(item => item.TenantId)
            .ToHashSet();
    }
}
