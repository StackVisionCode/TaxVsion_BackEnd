using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IModuleRepository
{
    Task<Module?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);
    Task<int> CountActiveSubscriptionAssignmentsAsync(Guid moduleId, CancellationToken ct = default);
    Task<int> CountActiveSubscriptionsUsingAsync(Guid moduleId, CancellationToken ct = default);
    Task AddAsync(Module module, CancellationToken ct = default);
    void Remove(Module module);

    // PlanModule link operations
    Task<bool> PlanModuleLinkExistsAsync(Guid moduleId, Guid planId, CancellationToken ct = default);
    Task AddPlanModuleAsync(PlanModule link, CancellationToken ct = default);
    Task<PlanModule?> GetPlanModuleLinkAsync(Guid moduleId, Guid planId, CancellationToken ct = default);
    void RemovePlanModule(PlanModule link);
}
