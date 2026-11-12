using BuildingBlocks.RateLimiting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Filas de rate limit por plan en memoria.</summary>
public sealed class InMemoryPlanRateLimitRepo(params PlanRateLimit[] rows) : IPlanRateLimitRepository
{
    public List<PlanRateLimit> Rows { get; } = [.. rows];

    public Task<IReadOnlyList<PlanRateLimit>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlanRateLimit>>(Rows.ToList());

    public Task<IReadOnlyList<PlanRateLimit>> GetByPlanCodeAsync(string planCode, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlanRateLimit>>(Rows.Where(row => row.PlanCode.Value == planCode).ToList());

    public Task<PlanRateLimit?> GetForUpdateAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    ) => Task.FromResult(Rows.FirstOrDefault(row => row.PlanCode.Value == planCode && row.Category == category));

    public Task AddAsync(PlanRateLimit rateLimit, CancellationToken ct = default)
    {
        Rows.Add(rateLimit);
        return Task.CompletedTask;
    }
}

/// <summary>El perfil por defecto en memoria.</summary>
public sealed class InMemoryRateLimitProfileRepo(RateLimitDefaultProfile? profile) : IRateLimitDefaultProfileRepository
{
    public Task<RateLimitDefaultProfile?> GetAsync(CancellationToken ct = default) => Task.FromResult(profile);

    public Task<RateLimitDefaultProfile?> GetForUpdateAsync(CancellationToken ct = default) => Task.FromResult(profile);
}
