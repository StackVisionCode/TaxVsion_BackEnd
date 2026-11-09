using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Domain.Reservations;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>IReservationRepository en memoria.</summary>
public sealed class FakeReservationRepository : IReservationRepository
{
    public List<WalletReservation> Reservations { get; } = [];

    public Task<WalletReservation?> GetByReferenceAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Reservations.FirstOrDefault(r =>
                r.TenantId == tenantId && r.ReferenceType == referenceType && r.ReferenceId == referenceId
            )
        );

    public Task<IReadOnlyList<WalletReservation>> ListOpenOlderThanAsync(
        DateTime cutoffUtc,
        int take,
        string? referenceType,
        IReadOnlyCollection<string>? excludedTypes,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<WalletReservation>>(
            Reservations
                .Where(r => r.Status == ReservationStatus.Open && r.CreatedAtUtc < cutoffUtc)
                .Where(r => referenceType is null || r.ReferenceType == referenceType)
                .Where(r => excludedTypes is null || !excludedTypes.Contains(r.ReferenceType))
                .OrderBy(r => r.CreatedAtUtc)
                .Take(take)
                .ToList()
        );

    public Task AddAsync(WalletReservation reservation, CancellationToken ct = default)
    {
        Reservations.Add(reservation);
        return Task.CompletedTask;
    }
}
