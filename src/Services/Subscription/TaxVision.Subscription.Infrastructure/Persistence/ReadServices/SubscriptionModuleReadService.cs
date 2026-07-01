using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.ReadServices;

public sealed class SubscriptionModuleReadService(SubscriptionDbContext db) : ISubscriptionModuleReadService
{
    public async Task<List<SubscriptionModuleDto>> GetBySubscriptionIdAsync(
        Guid subscriptionId, bool? isIncluded, CancellationToken ct = default)
    {
        return await (
            from sm in db.SubscriptionModules.AsNoTracking()
            join m in db.Modules.AsNoTracking() on sm.ModuleId equals m.Id
            where sm.SubscriptionId == subscriptionId
                  && (isIncluded == null || sm.IsIncluded == isIncluded)
            orderby sm.Id
            select new SubscriptionModuleDto
            {
                Id              = sm.Id,
                SubscriptionId  = sm.SubscriptionId,
                ModuleId        = sm.ModuleId,
                IsIncluded      = sm.IsIncluded,
                ModuleName      = m.Name,
                ModuleDescription = m.Description,
                ModuleUrl       = m.Url
            }).ToListAsync(ct);
    }
}
