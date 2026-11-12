using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Catálogo en memoria con el mismo orden que el repositorio real (rango y después código).</summary>
public sealed class InMemoryPlanRepo(params SubscriptionPlan[] plans) : IPlanRepository
{
    private IEnumerable<SubscriptionPlan> Ordered =>
        plans.OrderBy(plan => plan.SalesRank.Value).ThenBy(plan => plan.Code.Value, StringComparer.Ordinal);

    public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(
            Ordered.Where(plan => plan.Status == PlanStatus.Published).ToList()
        );

    public Task<IReadOnlyList<SubscriptionPlan>> GetOnSaleAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(
            Ordered.Where(plan => plan.Status == PlanStatus.Published && plan.IsOnSale).ToList()
        );

    public Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Ordered.ToList());

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(plans.FirstOrDefault(plan => plan.Code.Value == code));

    public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
        Task.FromResult(plans.FirstOrDefault(plan => plan.Id == planId));

    public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default) =>
        GetByIdAsync(planId, ct);

    public Task AddAsync(SubscriptionPlan plan, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(plans.Any(plan => plan.Code.Value == code));
}
