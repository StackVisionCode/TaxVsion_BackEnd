using TaxVision.Codes.Domain.Redemptions;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeRedemptionRepository
{
    Task<CodeRedemption?> GetByIdAsync(Guid tenantId, Guid redemptionId, CancellationToken ct = default);

    Task<CodeRedemption?> GetByReservationIdAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct = default
    );

    Task AddAsync(CodeRedemption redemption, CancellationToken ct = default);
}
