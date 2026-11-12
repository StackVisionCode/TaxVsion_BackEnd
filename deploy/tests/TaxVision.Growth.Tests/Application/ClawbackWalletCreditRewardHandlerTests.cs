using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El pago del referido se reembolsó o perdió un chargeback: el premio se cancela (si no se acreditó) o se PIDE su
/// reversa al Wallet por evento (si ya se acreditó) y se cierra cuando el Wallet responde. Un reembolso parcial no lo toca.
/// </summary>
public sealed class ClawbackWalletCreditRewardHandlerTests
{
    private static readonly DateTime AfterWait = GrowthTestData.NowUtc.AddDays(1).AddDays(31);

    [Fact]
    public async Task A_full_refund_before_crediting_cancels_the_reward_without_touching_the_wallet()
    {
        var f = await Fixture.CreateAsync(credited: false);

        var result = await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.Cancelled, result.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.Cancelled, f.Reward.Status);
        Assert.Empty(f.Wallet.Requests);
    }

    [Fact]
    public async Task A_full_refund_after_crediting_requests_the_reversal_and_waits_for_the_wallet()
    {
        var f = await Fixture.CreateAsync(credited: true);

        var result = await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.ReversalRequested, result.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.ClawbackPending, f.Reward.Status);
        var reversal = Assert.Single(f.Wallet.Requests.Where(r => r.Reverse));
        Assert.Equal(f.Reward.TenantId, reversal.BeneficiaryTenantId);
        Assert.Equal(f.Reward.RewardAmountCents, reversal.AmountCents);
        // El premio es del referidor: las escrituras se hacen en SU contexto de tenant.
        Assert.Equal(f.Reward.TenantId, f.Tenant.TenantId);
    }

    [Fact]
    public async Task When_the_wallet_answers_the_case_is_reversed_and_the_benefit_is_marked_reversed()
    {
        var f = await Fixture.CreateAsync(credited: true);
        await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        var confirmed = await f.ConfirmReversalAsync();

        Assert.Equal(ConfirmWalletReversalOutcome.Reversed, confirmed.Value);
        Assert.Equal(ReferralRewardCaseStatus.Reversed, f.Reward.Status);
        Assert.NotNull(Assert.Single(f.Benefits.Items).ReversedAtUtc);
    }

    [Fact]
    public async Task A_redelivered_reversal_answer_does_nothing()
    {
        var f = await Fixture.CreateAsync(credited: true);
        await f.RunAsync(refundedCents: f.Reward.PricePaidCents);
        await f.ConfirmReversalAsync();

        var again = await f.ConfirmReversalAsync();

        Assert.Equal(ConfirmWalletReversalOutcome.AlreadyReversed, again.Value);
    }

    [Fact]
    public async Task A_lost_chargeback_counts_as_a_total_loss()
    {
        var f = await Fixture.CreateAsync(credited: true);

        var result = await f.RunAsync(refundedCents: null);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.ReversalRequested, result.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.ClawbackPending, f.Reward.Status);
    }

    [Fact]
    public async Task A_partial_refund_keeps_the_reward()
    {
        var f = await Fixture.CreateAsync(credited: true);

        var result = await f.RunAsync(refundedCents: f.Reward.PricePaidCents - 1);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.PartialRefundIgnored, result.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.Granted, f.Reward.Status);
        Assert.DoesNotContain(f.Wallet.Requests, r => r.Reverse);
    }

    [Fact]
    public async Task A_payment_without_a_reward_is_a_no_op()
    {
        var f = await Fixture.CreateAsync(credited: true);
        f.Rewards.ByPayment.Clear();

        var result = await f.RunAsync(refundedCents: 1_000_000);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.NoReward, result.Value.Outcome);
        Assert.DoesNotContain(f.Wallet.Requests, r => r.Reverse);
    }

    [Fact]
    public async Task A_redelivered_event_while_the_reversal_is_pending_asks_the_wallet_again_which_is_idempotent()
    {
        var f = await Fixture.CreateAsync(credited: true);
        await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        var again = await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.ReversalRequested, again.Value.Outcome);
        Assert.Equal(2, f.Wallet.Requests.Count(r => r.Reverse));
    }

    [Fact]
    public async Task A_redelivered_event_after_the_reversal_does_nothing()
    {
        var f = await Fixture.CreateAsync(credited: true);
        await f.RunAsync(refundedCents: f.Reward.PricePaidCents);
        await f.ConfirmReversalAsync();

        var again = await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        Assert.Equal(ClawbackWalletCreditRewardOutcome.AlreadyHandled, again.Value.Outcome);
        Assert.Single(f.Wallet.Requests.Where(r => r.Reverse));
    }

    [Fact]
    public async Task The_worker_asks_again_for_a_reversal_that_the_wallet_never_answered()
    {
        var f = await Fixture.CreateAsync(credited: true);
        await f.RunAsync(refundedCents: f.Reward.PricePaidCents);

        var resend = await ResendWalletReversalHandler.Handle(
            new ResendWalletReversalCommand(f.Reward.TenantId, f.Reward.Id, GrowthTestData.ActorId),
            f.Rewards,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(AfterWait.AddDays(1).AddMinutes(10))),
            CancellationToken.None
        );
        if (resend.Value is { } resent)
            f.Wallet.Requests.Add(resent);

        Assert.True(resend.IsSuccess);
        Assert.Equal(2, f.Wallet.Requests.Count(r => r.Reverse));
        Assert.Equal(AfterWait.AddDays(1).AddMinutes(10), f.Reward.UpdatedAtUtc);
    }

    private sealed class Fixture
    {
        public required ReferralRewardCase Reward { get; init; }
        public required InMemoryReferralRewardCaseRepository Rewards { get; init; }
        public GrantWalletCreditRewardHandlerTests.InMemoryBenefitRepository Benefits { get; } = new();
        public GrantWalletCreditRewardHandlerTests.FakeWalletCreditRequests Wallet { get; } = new();
        public GrantWalletCreditRewardHandlerTests.CountingUnitOfWork UnitOfWork { get; } = new();
        public RecordingTenantContext Tenant { get; } = new();
        private readonly Guid _paymentId = Guid.NewGuid();
        private readonly GrantWalletCreditRewardHandlerTests.InMemoryAttemptRepository _attempts = new();

        public static async Task<Fixture> CreateAsync(bool credited)
        {
            var program = GrowthTestData.CreateActiveTenantReferralProgram();
            var attribution = GrowthTestData.CreateActiveAttribution(program, GrowthTestData.CreateReferralCode(program));
            var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
            Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);
            var reward = ReferralRewardCase
                .Request(
                    program,
                    attribution,
                    qualification,
                    "reward-clawback",
                    GrowthTestData.Sha('8'),
                    GrowthTestData.ActorId,
                    GrowthTestData.NowUtc.AddDays(1)
                )
                .Value;
            var rewards = new InMemoryReferralRewardCaseRepository(reward);
            var fixture = new Fixture { Reward = reward, Rewards = rewards };
            rewards.ByPayment[fixture._paymentId] = reward;

            if (credited)
            {
                // Crédito completo por evento: se pide al Wallet y su respuesta lo confirma.
                var requested = await GrantWalletCreditRewardHandler.Handle(
                    new GrantWalletCreditRewardCommand(reward.TenantId, reward.Id, GrowthTestData.ActorId),
                    rewards,
                    fixture._attempts,
                    fixture.UnitOfWork,
                    new FixedTimeProvider(new DateTimeOffset(AfterWait)),
                    NullLogger<GrantWalletCreditRewardCommand>.Instance,
                    CancellationToken.None
                );
                Assert.True(requested.IsSuccess);
                var confirmed = await ConfirmWalletCreditRewardHandler.Handle(
                    new ConfirmWalletCreditRewardCommand(
                        reward.TenantId,
                        reward.Id,
                        Guid.NewGuid(),
                        GrowthTestData.ActorId
                    ),
                    rewards,
                    fixture._attempts,
                    fixture.Benefits,
                    fixture.UnitOfWork,
                    new FixedTimeProvider(new DateTimeOffset(AfterWait.AddSeconds(5))),
                    NullLogger<ConfirmWalletCreditRewardCommand>.Instance,
                    CancellationToken.None
                );
                Assert.True(confirmed.IsSuccess);
                fixture.Wallet.Requests.Clear();
            }

            return fixture;
        }

        public async Task<Result<ClawbackWalletCreditRewardResult>> RunAsync(long? refundedCents)
        {
            var result = await ClawbackWalletCreditRewardHandler.Handle(
                new ClawbackWalletCreditRewardCommand(_paymentId, refundedCents, "refunded", GrowthTestData.ActorId),
                Rewards,
                Tenant,
                UnitOfWork,
                new FixedTimeProvider(new DateTimeOffset(AfterWait.AddDays(1))),
                NullLogger<ClawbackWalletCreditRewardCommand>.Instance,
                CancellationToken.None
            );
            if (result.IsSuccess && result.Value.Request is { } request)
                Wallet.Requests.Add(request);
            return result;
        }

        public Task<Result<ConfirmWalletReversalOutcome>> ConfirmReversalAsync() =>
            ConfirmWalletReversalHandler.Handle(
                new ConfirmWalletReversalCommand(Reward.TenantId, Reward.Id, GrowthTestData.ActorId),
                Rewards,
                Benefits,
                UnitOfWork,
                new FixedTimeProvider(new DateTimeOffset(AfterWait.AddDays(1).AddMinutes(1))),
                NullLogger<ConfirmWalletReversalCommand>.Instance,
                CancellationToken.None
            );
    }

    private sealed class RecordingTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool HasTenant => TenantId != Guid.Empty;

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
