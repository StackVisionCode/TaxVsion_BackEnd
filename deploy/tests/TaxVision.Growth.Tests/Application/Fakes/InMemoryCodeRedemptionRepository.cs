using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Redemptions;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeRedemptionRepository : ICodeRedemptionRepository
{
    private readonly List<CodeRedemption> _redemptions = [];

    internal IReadOnlyList<CodeRedemption> Redemptions => _redemptions;

    public Task<CodeRedemption?> GetByIdAsync(
        Guid tenantId,
        Guid redemptionId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _redemptions.SingleOrDefault(redemption =>
                redemption.TenantId == tenantId && redemption.Id == redemptionId
            )
        );

    public Task<CodeRedemption?> GetByReservationIdAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _redemptions.SingleOrDefault(redemption =>
                redemption.TenantId == tenantId
                && redemption.ReservationId == reservationId
            )
        );

    public Task AddAsync(CodeRedemption redemption, CancellationToken ct = default)
    {
        _redemptions.Add(redemption);
        return Task.CompletedTask;
    }
}
