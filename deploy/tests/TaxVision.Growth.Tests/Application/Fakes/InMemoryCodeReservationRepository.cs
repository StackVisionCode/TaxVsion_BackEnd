using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeReservationRepository : ICodeReservationRepository
{
    private readonly List<CodeReservation> _reservations = [];

    internal IReadOnlyList<CodeReservation> Reservations => _reservations;

    public Task<CodeReservation?> GetByIdAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _reservations.SingleOrDefault(reservation =>
                reservation.Id == reservationId && reservation.TenantId == tenantId
            )
        );

    public Task AddAsync(CodeReservation reservation, CancellationToken ct = default)
    {
        _reservations.Add(reservation);
        return Task.CompletedTask;
    }
}
