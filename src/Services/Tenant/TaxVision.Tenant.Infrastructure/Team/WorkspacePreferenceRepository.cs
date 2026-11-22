using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Infrastructure.Persistence;

namespace TaxVision.Tenant.Infrastructure.Team;

/// <summary>
/// IgnoreQueryFilters() + tenant explícito: los handlers de Wolverine corren sin tenant ambiental; el tenant viene
/// del JWT o del evento, nunca de un parámetro del cliente.
/// </summary>
public sealed class WorkspacePreferenceRepository(TenantDbContext db) : IWorkspacePreferenceRepository
{
    public Task<WorkspacePreference?> GetForUserAsync(Guid tenantId, Guid userId, string key, CancellationToken ct) =>
        db
            .WorkspacePreferences.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.PreferenceKey == key, ct);

    public Task<WorkspacePreference?> GetOfficeDefaultAsync(Guid tenantId, string key, CancellationToken ct) =>
        db
            .WorkspacePreferences.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == null && p.PreferenceKey == key, ct);

    public void Add(WorkspacePreference preference) => db.WorkspacePreferences.Add(preference);

    // ExecuteDeleteAsync corre fuera de la unidad de trabajo (desviación consciente del "preparar en memoria"): la
    // cadena de Wolverine ya es transaccional y así no se cargan filas solo para borrarlas.
    public Task<int> DeleteAllOfUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        db
            .WorkspacePreferences.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.UserId == userId)
            .ExecuteDeleteAsync(ct);
}
