using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Plan?> GetByServiceLevelAsync(ServiceLevel serviceLevel, CancellationToken ct = default);
    Task<IReadOnlyList<Plan>> GetAllActiveAsyn