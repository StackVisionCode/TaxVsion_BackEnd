using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeReservationRepository : ICodeReservationRepository
{
    private readonly List<CodeReservation> _reservations = [];

    internal IReadOnlyList<CodeReservation> Reservations => _reservations;

    public Task<CodeReservation?> GetByIdAsync(Guid tenantId, Guid reservationId, CancellationToken ct = default) =>
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

    public Task<IReadOnlyList<ExpiredReservationRef>> GetActiveExpiredAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<ExpiredReservationRef>>(
            _reservations
                .Where(r => r.Status == CodeReservationStatus.Active && r.ExpiresAtUtc <= nowUtc)
                .OrderBy(r => r.ExpiresAtUtc)
                .Take(batchSize)
                .Select(r => new ExpiredReservationRef(r.TenantId, r.Id))
                .ToList()
        );
}
