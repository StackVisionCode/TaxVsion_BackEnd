using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.DisputeHold;
using TaxVision.Referrals.Domain.Rewards;
using Fixture = TaxVision.Growth.Tests.Application.ClawbackWalletCreditRewardHandlerTests.Fixture;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Disputa del pago del referido: mientras está abierta, el premio que aún no se acreditó queda retenido. Si se pierde
/// se quita como hoy; si se gana, vuelve a esperar su acreditación. Una revisión del admin no se libera por una disputa.
/// </summary>
public sealed class ReferralRewardDisputeHoldTests
{
    private const string DisputeId = "chargeback:1";
    private static readonly DateTime DuringDispute = GrowthTestData.NowUtc.AddDays(5);

    [Fact]
    public async Task Open_dispute_holds_the_reward()
    {
        var f = await Fixture.CreateAsync(credited: false);

        var held = await HoldAsync(f, DisputeId);

        Assert.Equal(ReferralRewardDisputeHoldOutcome.Held, held.Value);
        Assert.Equal(ReferralRewardCaseStatus.ManualReview, f.Reward.Status);
        Assert.Equal(DisputeId, f.Reward.HeldForDisputeId);
    }

    [Fact]
    public async Task Won_dispute_restores_the_reward()
    {
        var f = await Fixture.CreateAsync(credited: false);
        await HoldAsync(f, DisputeId);

        var released = await ReleaseAsync(f, DisputeId);

        Assert.Equal(ReferralRewardDisputeHoldOutcome.Released, released.Value);
        Assert.Equal(ReferralRewardCaseStatus.Requested, f.Reward.Status);
        Assert.Null(f.Reward.HeldForDisputeId);
    }

    [Fact]
    public async Task Lost_dispute_claws_back_the_held_reward()
    {
        var f = await Fixture.CreateAsync(credited: false);
        await HoldAsync(f, DisputeId);

        var clawback = await f.RunAsync(refundedCents: null);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.Cancelled, clawback.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.Cancelled, f.Reward.Status);
        Assert.Empty(f.Wallet.Requests);
    }

    [Fact]
    public async Task An_open_dispute_leaves_a_credited_reward_until_it_is_decided()
    {
        var f = await Fixture.CreateAsync(credited: true);

        var held = await HoldAsync(f, DisputeId);

        Assert.Equal(ReferralRewardDisputeHoldOutcome.AlreadyCredited, held.Value);
        Assert.Equal(ReferralRewardCaseStatus.Granted, f.Reward.Status);
    }

    [Fact]
    public async Task A_won_dispute_does_not_release_an_admin_review()
    {
        var f = await Fixture.CreateAsync(credited: false);
        Assert.True(f.Reward.FlagForReview("Suspected abuse", GrowthTestData.ActorId, DuringDispute).IsSuccess);
        await HoldAsync(f, DisputeId);

        var released = await ReleaseAsync(f, DisputeId);

        Assert.Equal(ReferralRewardDisputeHoldOutcome.NotHeldByThisDispute, released.Value);
        Assert.Equal(ReferralRewardCaseStatus.ManualReview, f.Reward.Status);
    }

    [Fact]
    public async Task A_payment_without_a_reward_is_a_no_op()
    {
        var f = await Fixture.CreateAsync(credited: false);
        f.Rewards.ByPayment.Clear();

        Assert.Equal(ReferralRewardDisputeHoldOutcome.NoReward, (await HoldAsync(f, DisputeId)).Value);
        Assert.Equal(ReferralRewardDisputeHoldOutcome.NoReward, (await ReleaseAsync(f, DisputeId)).Value);
    }

    private static Task<Result<ReferralRewardDisputeHoldOutcome>> HoldAsync(Fixture f, string disputeId) =>
        HoldReferralRewardForDisputeHandler.Handle(
            new HoldReferralRewardForDisputeCommand(f.PaymentId, disputeId, GrowthTestData.ActorId),
            f.Rewards,
            f.Tenant,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(DuringDispute)),
            NullLogger<HoldReferralRewardForDisputeCommand>.Instance,
            CancellationToken.None
        );

    private static Task<Result<ReferralRewardDisputeHoldOutcome>> ReleaseAsync(Fixture f, string disputeId) =>
        ReleaseReferralRewardDisputeHoldHandler.Handle(
            new ReleaseReferralRewardDisputeHoldCommand(f.PaymentId, disputeId, GrowthTestData.ActorId),
            f.Rewards,
            f.Tenant,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(DuringDispute.AddDays(10))),
            NullLogger<ReleaseReferralRewardDisputeHoldCommand>.Instance,
            CancellationToken.None
        );
}
