using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class AddOnDefinitionRepository(SubscriptionDbContext db) : IAddOnDefinitionRepository
{
    public async Task<IReadOnlyList<AddOnDefinition>> GetPublishedAsync(CancellationToken ct = default) =>
        await WithChildren(db.AddOnDefinitions.AsNoTracking())
            .Where(definition => definition.Status == AddOnDefinitionStatus.Published)
            .ToListAsync(ct);

    public Task<AddOnDefinition?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        WithChildren(db.AddOnDefinitions.AsNoTracking()).FirstOrDefaultAsync(definition => definition.Code.Value == code, ct);

    public Task<AddOnDefinition?> GetByIdAsync(Guid addOnDefinitionId, CancellationToken ct = default) =>
        WithChildren(db.AddOnDefinitions.AsNoTracking()).FirstOrDefaultAsync(definition => definition.Id == addOnDefinitionId, ct);

    private static IQueryable<AddOnDefinition> WithChildren(IQueryable<AddOnDefinition> query) =>
        query
            .Include(definition => definition.Features)
            .Include(definition => definition.Entitlements)
            .Include(definition => definition.PriceTiers);
}
