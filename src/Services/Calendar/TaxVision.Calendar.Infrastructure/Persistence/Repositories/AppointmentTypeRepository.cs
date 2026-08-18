using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class AppointmentTypeRepository(CalendarDbContext context) : IAppointmentTypeRepository
{
    public async Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default)
    {
        var type = await Scoped(tenantId).FirstOrDefaultAsync(t => t.Id == typeId, ct);

        return type is null ? Result.Failure<AppointmentType>(TypeErrors.NotFound) : Result.Success(type);
    }

    public async Task<IReadOnlyList<AppointmentType>> ListAsync(
        Guid tenantId,
        bool onlyActive,
        CancellationToken ct = default
    ) => await Scoped(tenantId).Where(t => !onlyActive || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default) =>
        await Scoped(tenantId).AnyAsync(ct);

    public void Add(AppointmentType type) => context.AppointmentTypes.Add(type);

    private IQueryable<AppointmentType> Scoped(Guid tenantId) =>
        context.AppointmentTypes.IgnoreQueryFilters().Where(t => t.TenantId == tenantId);
}
