using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters() + tenant explícito: el consumer del acuse y el barrido no siempre corren con el
// TenantContext del mismo scope que el DbContext (ver lección "filter vs Wolverine").
public sealed class OffboardingRunRepository(AuthDbContext db) : IOffboardingRunRepository
{
    public async Task AddAsync(OffboardingRun run, CancellationToken ct) => await db.OffboardingRuns.AddAsync(run, ct);

    public Task<OffboardingRun?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        db
            .OffboardingRuns.IgnoreQueryFilters()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.UserId == userId, ct);

    public Task<OffboardingRun?> GetByIdAsync(Guid tenantId, Guid runId, CancellationToken ct) =>
        db
            .OffboardingRuns.IgnoreQueryFilters()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == runId, ct);

    public async Task<IReadOnlyList<OverdueOffboardingRun>> ListOverdueAsync(
        DateTime startedBeforeUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .OffboardingRuns.IgnoreQueryFilters()
            .Where(r =>
                r.IncompleteAtUtc == null
                && r.StartedAtUtc < startedBeforeUtc
                && r.Steps.Any(s => s.CompletedAtUtc == null)
            )
            .OrderBy(r => r.StartedAtUtc)
            .Take(take)
            .Select(r => new OverdueOffboardingRun(r.TenantId, r.Id))
            .ToListAsync(ct);
}
