using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(SubscriptionDbContext db) : IPlanRepository
{
    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Plans.Include(p => p.Features)
            .Include(p => p.PlanModules)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        db.Plans.Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);

    public Task<Plan?> GetByServiceLevelAsync(ServiceLevel serviceLevel, CancellationToken ct = default) =>
        db.Plans.Include(p => p.PlanModules)
            .FirstOrDefaultAsync(p => p.ServiceLevel == serviceLevel && p.IsActive, ct);

    public Task<IReadOnlyList<Plan>> GetAllActiveAsync(CancellationToken ct = default) =>
        db.Pl