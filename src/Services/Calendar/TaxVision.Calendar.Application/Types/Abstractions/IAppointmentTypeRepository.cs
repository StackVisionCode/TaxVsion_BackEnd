using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Types.Abstractions;

public interface IAppointmentTypeRepository
{
    Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default);

    Task<IReadOnlyList<AppointmentType>> ListAsync(Guid tenantId, bool onlyActive, CancellationToken ct = default);

    Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default);

    void Add(AppointmentType type);
}
