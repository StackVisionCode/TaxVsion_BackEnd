using TaxVision.Codes.Domain.Compensations;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeCompensationRepository
{
    /// <summary>
    /// Must be backed by a unique constraint on (TenantId, RedemptionId, SourceEventId).
    /// </summary>
    Task<CodeCompensation?> GetBySourceEventIdAsync(
        Guid tenantId,
        Guid redemptionId,
        Guid sourceEventId,
        CancellationToken ct = default
    );

    Task<long> GetCumulativeAdjustmentAmountCentsAsync(
        Guid tenantId,
        Guid redemptionId,
        CancellationToken ct = default
    );

    Task AddAsync(CodeCompensation compensation, CancellationToken ct = default);
}
