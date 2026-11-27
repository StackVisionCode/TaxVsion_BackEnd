using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Dueño de un buzón tal cual lo conoce Connectors. <c>IsActive</c>/<c>IsOffice</c> null: Connectors anterior a esos
/// campos. <c>IsOffice</c> es el dato de Connectors: Postmaster no lo infiere del dueño nulo.
/// </summary>
public sealed record ConnectorsMailboxOwner(Guid AccountId, Guid? OwnerUserId, bool? IsActive, bool? IsOffice = null);

/// <summary>Lectura M2M de los buzones de un tenant en Connectors (<c>internal/accounts/addresses</c>).</summary>
public interface IConnectorsMailboxDirectory
{
    Task<Result<IReadOnlyList<ConnectorsMailboxOwner>>> ListAsync(Guid tenantId, CancellationToken ct);
}
