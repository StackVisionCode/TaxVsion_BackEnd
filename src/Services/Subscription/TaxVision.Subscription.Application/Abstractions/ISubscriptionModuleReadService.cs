using TaxVision.Subscription.Application.SubscriptionModules.Dtos;

namespace TaxVision.Subscription.Application.Abstractions;

public interface ISubscriptionModuleReadService
{
    Task<List<SubscriptionModuleDto>> GetBySubscriptionIdAsync(
        Guid subscriptionId, bool? isIncluded, CancellationToken ct = default);
}
