using BuildingBlocks.Common;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Queries;

namespace TaxVision.Growth.Tests.Application;

/// <summary>Las consultas de "Mis referidos" validan tenant y paginación antes de tocar el modelo de lectura.</summary>
public sealed class ReferralQueryHandlerTests
{
    private sealed class FakeReadModel : IReferralReadModel
    {
        public int Calls { get; private set; }

        public Task<PagedResult<ReferralInviteRow>> ListInvitesAsync(
            Guid referrerTenantId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(new PagedResult<ReferralInviteRow>([], page, size, 0));
        }

        public Task<PagedResult<ReferralShareRow>> ListSharesAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(new PagedResult<ReferralShareRow>([], page, size, 0));
        }

        public Task<ReferralRewardSummary> GetSummaryAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ReferralRewardSummary(1290, 500, 3, 1, "USD"));
        }
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_paging_is_rejected_without_querying(int page, int size)
    {
        var readModel = new FakeReadModel();

        var invites = await ReferralQueryHandler.Handle(
            new ListReferralInvitesQuery(Guid.NewGuid(), page, size),
            readModel,
            CancellationToken.None
        );
        var shares = await ReferralQueryHandler.Handle(
            new ListReferralSharesQuery(Guid.NewGuid(), page, size),
            readModel,
            CancellationToken.None
        );

        Assert.True(invites.IsFailure);
        Assert.True(shares.IsFailure);
        Assert.Equal("Referrals.InvalidPaging", invites.Error.Code);
        Assert.Equal(0, readModel.Calls);
    }

    [Fact]
    public async Task An_empty_tenant_is_rejected()
    {
        var readModel = new FakeReadModel();

        var result = await ReferralQueryHandler.Handle(
            new GetReferralRewardSummaryQuery(Guid.Empty),
            readModel,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(0, readModel.Calls);
    }

    [Fact]
    public async Task A_valid_query_returns_the_read_model_result()
    {
        var readModel = new FakeReadModel();

        var result = await ReferralQueryHandler.Handle(
            new GetReferralRewardSummaryQuery(Guid.NewGuid()),
            readModel,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1290, result.Value.TotalEarnedCents);
        Assert.Equal(1, readModel.Calls);
    }
}
