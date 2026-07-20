using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Usage;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeUsageCounterRepository : ICodeUsageCounterRepository
{
    private readonly Dictionary<(Guid TenantId, Guid DefinitionId, CodeUsageDimension Dimension, string Scope), CodeUsageCounter> _counters = [];

    internal IReadOnlyCollection<CodeUsageCounter> Counters => _counters.Values;

    public Task<Result<CodeUsageCounter>> GetOrCreateForUpdateAsync(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey,
        long maxRedemptions,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var key = (tenantId, codeDefinitionId, dimension, scopeKey.Value);
        if (_counters.TryGetValue(key, out var existing))
            return Task.FromResult(Result.Success(existing));

        var created = CodeUsageCounter.Create(
            tenantId,
            codeDefinitionId,
            dimension,
            scopeKey,
            maxRedemptions,
            nowUtc
        );
        if (created.IsSuccess)
            _counters[key] = created.Value;

        return Task.FromResult(created);
    }

    internal CodeUsageCounter Get(
        Guid tenantId,
        Guid definitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey
    ) => _counters[(tenantId, definitionId, dimension, scopeKey.Value)];
}
