using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class ModuleRepository(SubscriptionDbContext db) : IModuleRepository
{
    public async Task<IReadOnlyList<Module>> GetAllAsync(CancellationToken ct = default) =>
        await db.Modules.AsNoTracking().OrderBy(module => module.Code).ToListAsync(ct);

    public Task<Module?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        db.Modules.AsNoTracking().FirstOrDefaultAsync(module => module.Code == code.Trim().ToLowerInvariant(), ct);

    public Task<Module?> GetByCodeForUpdateAsync(string code, CancellationToken ct = default) =>
        db.Modules.FirstOrDefaultAsync(module => module.Code == code.Trim().ToLowerInvariant(), ct);

    public async Task AddAsync(Module module, CancellationToken ct = default) => await db.Modules.AddAsync(module, ct);
}
