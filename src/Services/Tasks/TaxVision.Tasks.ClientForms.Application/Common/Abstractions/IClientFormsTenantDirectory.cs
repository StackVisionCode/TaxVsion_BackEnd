namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

// Tenants con el módulo planner según la proyección local de planes (solo plataforma).
public interface IClientFormsTenantDirectory
{
    Task<IReadOnlySet<Guid>> ListAvailableTenantIdsAsync(CancellationToken ct = default);
}
