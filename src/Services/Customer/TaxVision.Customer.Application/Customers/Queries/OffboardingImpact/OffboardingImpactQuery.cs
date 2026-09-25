using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;

// Pre-flight de impacto (punto 3.2): cuánto trabajo de este empleado en Customer habría que reasignar
// antes de retirarlo = clientes activos donde es preparador primary O tiene un acceso adicional asignado.
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int AssignedClients);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ICustomerRepository customers,
        CancellationToken ct
    )
    {
        var assignedClients = await customers.CountActiveByAssignedPreparerAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(assignedClients);
    }
}
