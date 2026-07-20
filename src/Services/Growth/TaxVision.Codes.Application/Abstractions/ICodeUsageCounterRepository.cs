using BuildingBlocks.Results;
using TaxVision.Codes.Domain.Usage;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeUsageCounterRepository
{
    /// <summary>
    /// Gets or creates and tracks one usage row inside the caller's current SQL transaction.
    /// Implementations must use a unique constraint on
    /// (TenantId, CodeDefinitionId, Dimension, ScopeKey), resolve concurrent creation by
    /// reloading the winner, and preserve RowVersion optimistic concurrency.
    /// </summary>
    Task<Result<CodeUsageCounter>> GetOrCreateForUpdateAsync(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey,
        long maxRedemptions,
        DateTime nowUtc,
        CancellationToken ct = default
    );
}
