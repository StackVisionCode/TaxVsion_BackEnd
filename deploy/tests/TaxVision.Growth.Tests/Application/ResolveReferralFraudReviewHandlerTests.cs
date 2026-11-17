using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Fraud.ResolveReview;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El PlatformAdmin decide una revisión de fraude: aprobarla devuelve el premio a la espera; rechazarla lo cancela y
/// rechaza la atribución. Una revisión ya decidida no se vuelve a decidir.
/// </summary>
public sealed class ResolveReferralFraudReviewHandlerTests
{
    [Fact]
    public async Task Approved_review_releases_the_case()
    {
        var f = await UnderReviewAsync();

        var resolved = await ResolveAsync(f, approve: true);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        Assert.Equal(FraudReviewStatus.Approved, Assert.Single(f.Reviews.Items).Status);
        Assert.Equal(ReferralRewardCaseStatus.Requested, f.Reward.Status);
        Assert.Equal(ReferralAttributionStatus.Qualified, f.Attribution.Status);
        Assert.False(resolved.Value.RewardCancelled);
    }

    [Fact]
    public async Task Rejected_review_cancels_the_case()
    {
        var f = await UnderReviewAsync();

        var resolved = await ResolveAsync(f, approve: false);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        Assert.Equal(FraudReviewStatus.Rejected, Assert.Single(f.Reviews.Items).Status);
        Assert.Equal(ReferralRewardCaseStatus.Cancelled, f.Reward.Status);
        Assert.Equal(ReferralAttributionStatus.Rejected, f.Attribution.Status);
        Assert.True(resolved.Value.RewardCancelled);
        Assert.Equal(f.Reward.TenantId, resolved.Value.RewardTenantId);
    }

    [Fact]
    public async Task A_decided_review_cannot_be_decided_again()
    {
        var f = await UnderReviewAsync();
        await ResolveAsync(f, approve: true);

        var again = await ResolveAsync(f, approve: false);

        Assert.Equal("FraudReview.InvalidTransition", again.Error.Code);
        Assert.Equal(ReferralRewardCaseStatus.Requested, f.Reward.Status);
    }

    [Fact]
    public async Task An_unknown_review_is_not_found()
    {
        var f = new ScreenReferralHandlerTests.Fixture();

        var resolved = await ResolveReferralFraudReviewHandler.Handle(
            new ResolveReferralFraudReviewCommand(Guid.NewGuid(), Approve: true, "ok", GrowthTestData.ActorId),
            f.Reviews,
            f.Attributions,
            f.Rewards,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("ReferralFraudReview.NotFound", resolved.Error.Code);
    }

    private static async Task<ScreenReferralHandlerTests.Fixture> UnderReviewAsync()
    {
        var f = new ScreenReferralHandlerTests.Fixture();
        f.Owners.Signals[f.Office] = new OfficeOwnerSignals("v1:same", OwnerEmailDisposable: false);
        f.Owners.Signals[GrowthTestData.ReferrerTenantId] = new OfficeOwnerSignals("v1:same", false);
        f.Link(Guid.NewGuid());
        Assert.Equal(ReferralFraudVerdict.Review, (await f.RunAsync()).Value.Verdict);
        return f;
    }

    private static Task<BuildingBlocks.Results.Result<ReferralFraudReviewResolution>> ResolveAsync(
        ScreenReferralHandlerTests.Fixture f,
        bool approve
    ) =>
        ResolveReferralFraudReviewHandler.Handle(
            new ResolveReferralFraudReviewCommand(
                f.Reviews.Items.Single().Id,
                approve,
                approve ? "Different people, same family." : "Same owner.",
                GrowthTestData.ActorId
            ),
            f.Reviews,
            f.Attributions,
            f.Rewards,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddHours(1))),
            CancellationToken.None
        );
}
