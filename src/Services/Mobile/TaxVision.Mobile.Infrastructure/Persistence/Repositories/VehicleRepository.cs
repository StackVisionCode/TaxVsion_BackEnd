using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Infrastructure.Persistence.Repositories;

public sealed class VehicleRepository(MobileDbContext db) : IVehicleRepository
{
    // Sin filtro global: la oficina y el cliente van explícitos, y vacíos no devuelven nada.
    private IQueryable<Vehicle> Owned(Guid tenantId, Guid customerId) =>
        db
            .Vehicles.IgnoreQueryFilters()
            .Where(x =>
                tenantId != Guid.Empty
                && customerId != Guid.Empty
                && x.TenantId == tenantId
                && x.CustomerId == customerId
            );

    public Task<Vehicle?> FindAsync(Guid tenantId, Guid customerId, Guid id, CancellationToken ct) =>
        Owned(tenantId, customerId).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<Vehicle>> ListAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct
    )
    {
        var query = Owned(tenantId, customerId).AsNoTracking();
        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new(items, page, size, count);
    }

    public async Task ClearDefaultAsync(Guid tenantId, Guid customerId, DateTime nowUtc, CancellationToken ct)
    {
        var defaults = await Owned(tenantId, customerId).Where(x => x.IsDefault).ToListAsync(ct);
        foreach (var vehicle in defaults)
            vehicle.ClearDefault(nowUtc);
        // Libera el índice filtrado antes de asignar el nuevo favorito, dentro de la transacción de Wolverine.
        if (defaults.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    public void Add(Vehicle vehicle) => db.Vehicles.Add(vehicle);

    public void Remove(Vehicle vehicle) => db.Vehicles.Remove(vehicle);
}
