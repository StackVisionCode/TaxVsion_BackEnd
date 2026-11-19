using BuildingBlocks.Common;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Abstractions;

public interface IVehicleRepository
{
    Task<Vehicle?> FindAsync(Guid tenantId, Guid customerId, Guid id, CancellationToken ct);
    Task<PagedResult<Vehicle>> ListAsync(Guid tenantId, Guid customerId, int page, int size, CancellationToken ct);
    Task ClearDefaultAsync(Guid tenantId, Guid customerId, DateTime nowUtc, CancellationToken ct);
    void Add(Vehicle vehicle);
    void Remove(Vehicle vehicle);
}
