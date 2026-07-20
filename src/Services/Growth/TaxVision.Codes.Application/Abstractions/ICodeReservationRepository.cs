using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeReservationRepository
{
    Task<CodeReservation?> GetByIdAsync(Guid tenantId, Guid reservationId, CancellationToken ct = default);

    Task AddAsync(CodeReservation reservation, CancellationToken ct = default);
}
