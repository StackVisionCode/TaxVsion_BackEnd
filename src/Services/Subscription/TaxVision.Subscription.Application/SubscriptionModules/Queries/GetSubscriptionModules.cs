using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;

namespace TaxVision.Subscription.Application.SubscriptionModules.Queries;

public record GetSubscriptionModulesQuery(Guid SubscriptionId, bool? IsIncluded = null);

public static class GetSubscriptionModulesHandler
{
    public static Task<List<SubscriptionModuleDto>> Handle(
        GetSubscriptionModulesQuery query,
        ISubscriptionModuleReadService readService,
        CancellationToken ct)
        => readService.GetBySubscriptionIdAsync(query.SubscriptionId, query.IsIncluded, ct);
}
