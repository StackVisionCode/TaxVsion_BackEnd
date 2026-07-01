using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository(SubscriptionDbContext db) : ISubscriptionRepository
{
    public Task<Domain.Subscriptions.Subscription?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Subscriptions.Include("_seats")
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Domain.Subscriptions.Subscription?> GetActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Subscriptions.Include("_seats")
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId &&
                     s.Status != Domain.Subscriptions.SubscriptionStatus.Cancelled, ct);

    public Task<bool> ExistsForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Subscriptions.AsNoTracki