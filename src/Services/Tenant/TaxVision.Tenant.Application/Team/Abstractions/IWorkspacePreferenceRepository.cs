using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Abstractions;

/// <summary>
/// Preferencias de espacio de trabajo. Todas las consultas llevan el tenant explícito (corren sin tenant ambiental).
/// Usuario y plantilla van por métodos separados para no comparar un parámetro nulo.
/// </summary>
public interface IWorkspacePreferenceRepository
{
    Task<WorkspacePreference?> GetForUserAsync(Guid tenantId, Guid userId, string key, CancellationToken ct);

    Task<WorkspacePreference?> GetOfficeDefaultAsync(Guid tenantId, string key, CancellationToken ct);

    void Add(WorkspacePreference preference);

    /// <summary>Borra todas las filas del usuario en esa oficina (no la plantilla). Devuelve cuántas borró.</summary>
    Task<int> DeleteAllOfUserAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
