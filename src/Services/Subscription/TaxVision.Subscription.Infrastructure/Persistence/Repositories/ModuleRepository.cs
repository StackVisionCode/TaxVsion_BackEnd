using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class ModuleRepository(SubscriptionDbContext db) : IModuleRepository
{
    public Task<Module?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Modules.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        db.Modules.AsNoTracking().AnyAsync(m => m.Id == id, ct);

    public Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default) =>
        excludeId.HasValue
            ? db.Modules.AsNoTracking().AnyAsync(m => m.Name == name && m.Id != excludeId.Value, ct)
            : db.Modules.AsNoTracking().AnyAsync(m => m.Name == name, ct);

    public Task<int> CountActiveSubscriptionAssignmentsAsync(Guid moduleId, CancellationToken ct = default) =>
        db.SubscriptionModules.AsNoTracking()
            .CountAsync(sm => sm.ModuleId == moduleId && sm.IsIncluded, ct);

    public Task<int> CountActiveSubscriptionsUsingAsync(Guid moduleId, CancellationToken ct = default) =>
        db.SubscriptionModules.AsNoTracking()
            .Where(sm => sm.ModuleId == moduleId && sm.IsIncluded)
            .Select(sm => sm.SubscriptionId)
            .Distinct()
            .CountAsync(ct);

    public async Task AddAsync(Module module, CancellationToken ct = default) =>
        await db.Modules.AddAsync(module, ct);

    public void Remove(Module module) =>
        db.Modules.Remove(module);

    public Task<bool> PlanModuleLinkExistsAsync(Guid moduleId, Guid planId, CancellationToken ct = default) =>
        db.PlanModules.AsNoTracking()
            .AnyAsync(pm => pm.ModuleId == moduleId && pm.PlanId == planId, ct);

    public async Task AddPlanModuleAsync(PlanModule link, CancellationToken ct = default) =>
        await db.PlanModules.AddAsync(link, ct);

    public Task<PlanModule?> GetPlanModuleLinkAsync(Guid moduleId, Guid planId, CancellationToken ct = default) =>
        db.PlanModules.FirstOrDefaultAsync(pm => pm.ModuleId == moduleId && pm.PlanId == planId, ct);

    public void RemovePlanModule(PlanModule link) =>
        db.PlanModules.Remove(link);
}
