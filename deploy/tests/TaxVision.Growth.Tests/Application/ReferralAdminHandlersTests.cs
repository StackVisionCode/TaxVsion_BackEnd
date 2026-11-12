using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Admin;
using TaxVision.Referrals.Application.Codes.RevokeReferralCode;
using TaxVision.Referrals.Application.Rewards.ReviewRewardCase;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Administración de referidos por la plataforma: revisar premios (retener / visto bueno / rechazar), revocar el código
/// de una oficina sin reemplazo y listar con paginación y filtro validados.
/// </summary>
public sealed class ReferralAdminHandlersTests
{
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(GrowthTestData.NowUtc.AddDays(2)));

    private static ReferralRewardCase NewReward()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = GrowthTestData.CreateActiveAttribution(program, GrowthTestData.CreateReferralCode(program));
        var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);
        return ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "review-case",
                GrowthTestData.Sha('7'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;
    }

    private static Task<BuildingBlocks.Results.Result<ReviewRewardCaseResult>> Review(
        ReferralRewardCase reward,
        RewardReviewAction action,
        string reason = "suspicious",
        InMemoryReferralRewardCaseRepository? repo = null,
        Guid? rewardId = null
    ) =>
        ReviewRewardCaseHandler.Handle(
            new ReviewRewardCaseCommand(reward.TenantId, rewardId ?? reward.Id, action, reason, GrowthTestData.ActorId),
            repo ?? new InMemoryReferralRewardCaseRepository(reward),
            new Counting(),
            Clock,
            CancellationToken.None
        );

    private sealed class Counting : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task Flagging_holds_a_pending_reward_for_review()
    {
        var reward = NewReward();

        var result = await Review(reward, RewardReviewAction.Flag);

        Assert.Equal(ReferralRewardCaseStatus.ManualReview, result.Value.Status);
        Assert.Equal("suspicious", reward.StateReason);
    }

    [Fact]
    public async Task Releasing_returns_the_reward_to_the_normal_queue()
    {
        var reward = NewReward();
        await Review(reward, RewardReviewAction.Flag);

        var result = await Review(reward, RewardReviewAction.Release, "checked, legitimate");

        Assert.Equal(ReferralRewardCaseStatus.Requested, result.Value.Status);
    }

    [Fact]
    public async Task Rejecting_cancels_a_reward_under_review()
    {
        var reward = NewReward();
        await Review(reward, RewardReviewAction.Flag);

        var result = await Review(reward, RewardReviewAction.Reject, "self referral");

        Assert.Equal(ReferralRewardCaseStatus.Cancelled, result.Value.Status);
    }

    [Fact]
    public async Task A_reward_that_is_not_under_review_cannot_be_released()
    {
        var reward = NewReward();

        var result = await Review(reward, RewardReviewAction.Release);

        Assert.Equal("ReferralReward.InvalidTransition", result.Error.Code);
        Assert.Equal(ReferralRewardCaseStatus.Requested, reward.Status);
    }

    [Fact]
    public async Task A_reward_already_in_clawback_cannot_be_flagged_released_or_cancelled_by_review()
    {
        var reward = NewReward();
        Assert.True(reward.BeginGrant(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(40)).IsSuccess);
        Assert.True(
            reward
                .ConfirmGranted("wallet-credit:abc", GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(40))
                .IsSuccess
        );
        Assert.True(
            reward.RequestClawback("refund", GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(41)).IsSuccess
        );
        Assert.True(
            reward
                .MoveToManualReview("wallet unreachable", GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(42))
                .IsSuccess
        );

        var release = await Review(reward, RewardReviewAction.Release);
        var reject = await Review(reward, RewardReviewAction.Reject);

        Assert.Equal("ReferralReward.InvalidTransition", release.Error.Code);
        Assert.Equal("ReferralReward.InvalidTransition", reject.Error.Code);
        Assert.Equal(ReferralRewardCaseStatus.ManualReview, reward.Status);
    }

    [Fact]
    public async Task A_review_needs_a_reason_and_an_existing_reward()
    {
        var reward = NewReward();

        var noReason = await Review(reward, RewardReviewAction.Flag, reason: "  ");
        var unknown = await Review(reward, RewardReviewAction.Flag, rewardId: Guid.NewGuid());

        Assert.Equal("ReferralReward.InvalidReason", noReason.Error.Code);
        Assert.Equal("ReferralReward.NotFound", unknown.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<RevokeReferralCodeResult>> Revoke(
        ReferralCode code,
        string reason = "abuse"
    ) =>
        RevokeReferralCodeHandler.Handle(
            new RevokeReferralCodeCommand(code.TenantId, code.Id, reason, GrowthTestData.ActorId),
            new InMemoryReferralCodeRepository(code),
            new Counting(),
            Clock,
            CancellationToken.None
        );

    [Fact]
    public async Task The_platform_can_revoke_an_office_code_without_replacing_it()
    {
        var code = GrowthTestData.CreateReferralCode(GrowthTestData.CreateActiveTenantReferralProgram());

        var result = await Revoke(code);

        Assert.Equal(ReferralCodeStatus.Revoked, result.Value.Status);
        Assert.Equal("abuse", code.RevocationReason);
    }

    [Fact]
    public async Task Revoking_needs_a_reason_and_an_existing_code()
    {
        var code = GrowthTestData.CreateReferralCode(GrowthTestData.CreateActiveTenantReferralProgram());

        var noReason = await Revoke(code, reason: "");
        var missing = await RevokeReferralCodeHandler.Handle(
            new RevokeReferralCodeCommand(code.TenantId, Guid.NewGuid(), "abuse", GrowthTestData.ActorId),
            new InMemoryReferralCodeRepository(code),
            new Counting(),
            Clock,
            CancellationToken.None
        );

        Assert.Equal("ReferralCode.InvalidReason", noReason.Error.Code);
        Assert.Equal("ReferralCode.NotFound", missing.Error.Code);
    }

    private sealed class EmptyReadModel : IReferralAdminReadModel
    {
        public int Calls { get; private set; }

        public Task<PagedResult<ReferralCodeAdminRow>> ListCodesAsync(
            Guid? ownerTenantId,
            ReferralCodeStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(new PagedResult<ReferralCodeAdminRow>([], page, size, 0));
        }

        public Task<PagedResult<ReferralRewardAdminRow>> ListRewardsAsync(
            ReferralRewardCaseStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(new PagedResult<ReferralRewardAdminRow>([], page, size, 0));
        }
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_paging_is_rejected_without_querying(int page, int size)
    {
        var readModel = new EmptyReadModel();

        var codes = await ReferralAdminQueryHandler.Handle(
            new ListReferralCodesAdminQuery(null, null, page, size),
            readModel,
            default
        );
        var rewards = await ReferralAdminQueryHandler.Handle(
            new ListReferralRewardsAdminQuery(null, page, size),
            readModel,
            default
        );

        Assert.Equal("Referrals.InvalidPaging", codes.Error.Code);
        Assert.Equal("Referrals.InvalidPaging", rewards.Error.Code);
        Assert.Equal(0, readModel.Calls);
    }

    [Fact]
    public async Task An_unknown_status_filter_is_rejected_and_a_known_one_lists()
    {
        var readModel = new EmptyReadModel();

        var bad = await ReferralAdminQueryHandler.Handle(
            new ListReferralCodesAdminQuery(null, "Bogus"),
            readModel,
            default
        );
        var badReward = await ReferralAdminQueryHandler.Handle(
            new ListReferralRewardsAdminQuery("Bogus"),
            readModel,
            default
        );
        var good = await ReferralAdminQueryHandler.Handle(
            new ListReferralRewardsAdminQuery("manualreview"),
            readModel,
            default
        );

        Assert.Equal("Referrals.InvalidStatus", bad.Error.Code);
        Assert.Equal("Referrals.InvalidStatus", badReward.Error.Code);
        Assert.True(good.IsSuccess);
    }
}
