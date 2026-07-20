using BuildingBlocks.Results;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Growth.Tests.Application;

public sealed class ReferralsIdempotencyApplicationTests
{
    [Fact]
    public async Task Qualify_replays_the_exact_qualified_result_without_reserving_twice()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var attributions = new InMemoryReferralAttributionRepository(attribution);
        var programs = new InMemoryReferralProgramRepository(program);
        var qualifications = new InMemoryReferralQualificationRepository();
        var rewards = new InMemoryReferralRewardCaseRepository();
        var quota = new FakeReferralRewardQuota();
        var idempotency = new FakeReferralIdempotencyExecutor(
            qualifications,
            rewards,
            quota
        );
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(GrowthTestData.NowUtc.AddDays(1))
        );
        var command = new QualifyReferralCommand(
            GrowthTestData.RefereeTenantId,
            attribution.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            QualifyingPaymentSource.PaymentApp,
            2_500,
            "USD",
            IsFirstSuccessfulPayment: true,
            GrowthTestData.NowUtc.AddDays(1),
            "producer-key-qualified",
            GrowthTestData.ActorId
        );

        var first = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var replay = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(ReferralQualificationDecision.Qualified, first.Value.Decision);
        Assert.NotNull(first.Value.RewardCaseId);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
        Assert.Single(qualifications.Items);
        Assert.Single(rewards.Items);
        Assert.Equal(1, quota.InvocationCount);
        Assert.Single(quota.QualificationReservations);
    }

    [Fact]
    public async Task Qualify_replays_the_exact_rejected_result_and_conflicts_on_changed_payload()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var attributions = new InMemoryReferralAttributionRepository(attribution);
        var programs = new InMemoryReferralProgramRepository(program);
        var qualifications = new InMemoryReferralQualificationRepository();
        var rewards = new InMemoryReferralRewardCaseRepository();
        var quota = new FakeReferralRewardQuota();
        var idempotency = new FakeReferralIdempotencyExecutor(
            qualifications,
            rewards,
            quota
        );
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(GrowthTestData.NowUtc.AddDays(1))
        );
        var eventId = Guid.NewGuid();
        var command = new QualifyReferralCommand(
            GrowthTestData.RefereeTenantId,
            attribution.Id,
            eventId,
            Guid.NewGuid(),
            QualifyingPaymentSource.PaymentApp,
            2_500,
            "USD",
            IsFirstSuccessfulPayment: false,
            GrowthTestData.NowUtc.AddDays(1),
            "producer-key-one",
            GrowthTestData.ActorId
        );

        var first = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var replay = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            timeProvider,
            CancellationToken.None
        );
        var conflict = await QualifyReferralHandler.Handle(
            command with { PaymentAmountCents = 2_501, IdempotencyKey = "producer-key-two" },
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(ReferralQualificationDecision.Rejected, first.Value.Decision);
        Assert.Equal("NotFirstSuccessfulPayment", first.Value.RejectionReasonCode);
        Assert.Null(first.Value.RewardCaseId);
        Assert.False(first.Value.WasReplay);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Referrals.IdempotencyConflict", conflict.Error.Code);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
        Assert.Single(qualifications.Items);
        Assert.Empty(rewards.Items);
        Assert.Equal(0, quota.InvocationCount);
    }

    [Fact]
    public async Task Failed_commit_after_claim_rolls_back_writes_and_does_not_poison_replay_key()
    {
        const string plaintextCode = "FRIEND-2026";
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var programs = new InMemoryReferralProgramRepository(program);
        var codes = new InMemoryReferralCodeRepository(code);
        var attributions = new InMemoryReferralAttributionRepository();
        var hasher = new FakeReferralCodeTokenHasher(plaintextCode, code.CodeHash);
        var idempotency = new FakeReferralIdempotencyExecutor(attributions)
        {
            FailNextCommit = new Error(
                "Tests.CommitFailed",
                "The simulated transaction could not commit."
            ),
        };
        var command = new CreateReferralAttributionCommand(
            GrowthTestData.RefereeTenantId,
            program.Id,
            plaintextCode,
            ReferralParticipantType.Tenant,
            GrowthTestData.RefereeTenantId,
            "attribution-operation-one",
            GrowthTestData.ActorId
        );
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(GrowthTestData.NowUtc.AddMinutes(1))
        );

        var failed = await CreateReferralAttributionHandler.Handle(
            command,
            programs,
            codes,
            attributions,
            hasher,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(failed.IsFailure);
        Assert.Equal("Tests.CommitFailed", failed.Error.Code);
        Assert.Empty(attributions.Items);
        Assert.Equal(0, idempotency.StoredResponseCount);

        var retried = await CreateReferralAttributionHandler.Handle(
            command,
            programs,
            codes,
            attributions,
            hasher,
            idempotency,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(retried.IsSuccess);
        Assert.Single(attributions.Items);
        Assert.Equal(2, idempotency.ClaimedCount);
        Assert.Equal(2, idempotency.ExecutedBodyCount);
        Assert.Equal(1, idempotency.StoredResponseCount);
        Assert.Equal([plaintextCode, plaintextCode], hasher.ReceivedTokens);
        Assert.DoesNotContain(plaintextCode, command.ToString(), StringComparison.Ordinal);
    }
}
