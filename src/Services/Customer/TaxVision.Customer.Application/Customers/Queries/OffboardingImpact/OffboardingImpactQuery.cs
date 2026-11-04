using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;

// Pre-flight de impacto (punto 3.2): qué pasa en Customer si se retira a este empleado.
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

/// <param name="AssignedClients">Clientes activos con él como primary O con acceso adicional (retrocompatible:
/// es PrimaryClients + AdditionalAccess).</param>
/// <param name="PrimaryClients">Clientes activos donde es el preparador primary: pasan al sucesor o quedan sin preparador.</param>
/// <param name="AdditionalAccess">Clientes activos donde solo tiene acceso adicional: se le revoca, no se reasigna.</param>
/// <param name="ArchivedClients">Clientes archivados donde figura: solo se le suelta (al reactivarlos, la oficina asigna).</param>
public sealed record OffboardingImpactResponse(
    int AssignedClients,
    int PrimaryClients,
    int AdditionalAccess,
    int ArchivedClients
);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ICustomerRepository customers,
        CancellationToken ct
    )
    {
        var assignedClients = await customers.CountActiveByAssignedPreparerAsync(query.TenantId, query.UserId, ct);
        var counts = await customers.CountOffboardingImpactAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(
            assignedClients,
            counts.PrimaryClients,
            counts.AdditionalAccess,
            counts.ArchivedClients
        );
    }
}
