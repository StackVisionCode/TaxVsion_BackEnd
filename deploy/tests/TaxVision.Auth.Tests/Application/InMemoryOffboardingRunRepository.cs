using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Doble en memoria de las corridas del retiro con acuse.</summary>
internal sealed class InMemoryOffboardingRunRepository(params OffboardingRun[] seed) : IOffboardingRunRepository
{
    public List<OffboardingRun> Runs { get; } = [.. seed];

    public Task AddAsync(OffboardingRun run, CancellationToken ct)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<OffboardingRun?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        Task.FromResult(Runs.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId));

    public Task<OffboardingRun?> GetByIdAsync(Guid tenantId, Guid runId, CancellationToken ct) =>
        Task.FromResult(Runs.FirstOrDefault(r => r.TenantId == tenantId && r.Id == runId));

    public Task<IReadOnlyList<OverdueOffboardingRun>> ListOverdueAsync(
        DateTime startedBeforeUtc,
        int take,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<OverdueOffboardingRun>>(
            Runs.Where(r => r.IncompleteAtUtc is null && r.StartedAtUtc < startedBeforeUtc && !r.AllCompleted)
                .Take(take)
                .Select(r => new OverdueOffboardingRun(r.TenantId, r.Id))
                .ToList()
        );
}
