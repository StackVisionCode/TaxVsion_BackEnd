using BuildingBlocks.CustomerVisibility;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Impl del gate de detalle: flag apagado o actor con view_all ⇒ visible; en otro caso, visible solo si alguno de
/// los clientes mapeados en los firmantes está asignado al actor en la proyección local. Mismo criterio que el
/// filtro de la lista (<see cref="SignatureRequestReadService"/>).
/// </summary>
internal sealed class SignatureRequestVisibilityGate(
    ICustomerAssignmentProjectionStore assignments,
    IOptions<SignatureVisibilityOptions> visibility
) : ISignatureRequestVisibilityGate
{
    public Task<bool> CanActorSeeAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        IReadOnlyCollection<Guid> mappedCustomerIds,
        CancellationToken ct = default
    )
    {
        if (!visibility.Value.Enabled || canViewAll)
            return Task.FromResult(true);
        return assignments.IsAnyAssignedToUserAsync(tenantId, actorUserId, mappedCustomerIds, ct);
    }
}
