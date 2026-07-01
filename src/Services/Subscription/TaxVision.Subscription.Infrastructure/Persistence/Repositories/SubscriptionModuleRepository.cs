using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionModuleRepository(SubscriptionDbContext db) : ISubscriptionModuleRepository
{
    public async Task AddAsync(SubscriptionModule module, CancellationToken ct = default) =>
        await db.SubscriptionModules.AddAsync(module, ct);

    public Task<SubscriptionModule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.SubscriptionModules.FirstOrDefaultAsync(sm => sm.Id == id, ct);

    public Task<SubscriptionModule?> GetBySubscriptionAndModuleAsync(
        Guid subscriptionId, 