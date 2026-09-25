using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Repo de seats que captura los <c>AddAsync</c> (para verificar aprovisionamiento) y los devuelve
/// desde <c>GetByTenantIdAsync</c>. El resto de la superficie lanza.</summary>
public sealed class CapturingSeatRepo : ISubscriptionSeatRepository
{
    public List<SubscriptionSeat> Added { get; } = [];

    public Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default)
    {
        Added.Add(seat);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionSeat>>(Added);

    public Task<SubscriptionSeat?> GetByIdAsync(Guid id, Guid t, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid t, Guid u, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SubscriptionSeat?> GetTrackedByCurrentUserIdAsync(Guid t, Guid u, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetPastGracePeriodAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
        DateTime c,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
        DateTime f,
        DateTime t,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
        int p,
        int s,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}
