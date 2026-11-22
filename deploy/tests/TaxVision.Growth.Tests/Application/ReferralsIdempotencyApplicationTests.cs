using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Growth.Api.IntegrationEvents;
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
    private static readonly ReferralNoticeEvents Events = new();

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
        var idempotency = new FakeReferralIdempotencyExecutor(qualifications, rewards, quota);
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddDays(1)));
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
            GrowthTestData.ActorId,
            "corr-qualify"
        );

        var (first, firstNotice) = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            Events,
            timeProvider,
            CancellationToken.None
        );
        var (replay, replayNotice) = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            Events,
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
        // El aviso sale con la primera ejecución (en su transacción); la repetición no lo repite.
        var qualified = Assert.IsType<ReferralQualifiedIntegrationEvent>(firstNotice);
        Assert.Equal(command.PaymentId, qualified.QualifyingPaymentId);
        Assert.Equal(first.Value.RewardCaseId, qualified.AggregateId);
        Assert.Equal("corr-qualify", qualified.CorrelationId);
        Assert.Null(replayNotice);
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
        var idempotency = new FakeReferralIdempotencyExecutor(qualifications, rewards, quota);
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddDays(1)));
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
            GrowthTestData.ActorId,
            "corr-qualify"
        );

        var (first, _) = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            Events,
            timeProvider,
            CancellationToken.None
        );
        var (replay, _) = await QualifyReferralHandler.Handle(
            command,
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            Events,
            timeProvider,
            CancellationToken.None
        );
        var (conflict, _) = await QualifyReferralHandler.Handle(
            command with
            {
                PaymentAmountCents = 2_501,
                IdempotencyKey = "producer-key-two",
            },
            attributions,
            programs,
            qualifications,
            rewards,
            quota,
            idempotency,
            Events,
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
            FailNextCommit = new Error("Tests.CommitFailed", "The simulated transaction could not commit."),
        };
        var command = new CreateReferralAttributionCommand(
            GrowthTestData.RefereeTenantId,
            program.Id,
            plaintextCode,
            ReferralParticipantType.Tenant,
            GrowthTestData.RefereeTenantId,
            "attribution-operation-one",
            GrowthTestData.ActorId,
            NoticeCorrelationId: "corr-attribution"
        );
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddMinutes(1)));

        var (failed, failedNotice) = await CreateReferralAttributionHandler.Handle(
            command,
            programs,
            codes,
            attributions,
            hasher,
            idempotency,
            new NoShares(),
            Events,
            timeProvider,
            CancellationToken.None
        );

        Assert.True(failed.IsFailure);
        Assert.Equal("Tests.CommitFailed", failed.Error.Code);
        Assert.Empty(attributions.Items);
        Assert.Equal(0, idempotency.StoredResponseCount);
        // Sin commit no hay aviso: viaja en la misma unidad que la atribución.
        Assert.Null(failedNotice);

        var (retried, retriedNotice) = await CreateReferralAttributionHandler.Handle(
            command,
            programs,
            codes,
            attributions,
            hasher,
            idempotency,
            new NoShares(),
            Events,
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
        var attributed = Assert.IsType<ReferralAttributedIntegrationEvent>(retriedNotice);
        Assert.Equal(retried.Value.AttributionId, attributed.AttributionId);
        Assert.Equal(code.OwnerId, attributed.TenantId);
        Assert.Equal("corr-attribution", attributed.CorrelationId);
    }

    /// <summary>
    /// El aviso al referidor vuelve como mensaje en cascada y sale por el outbox de la misma transacción; una
    /// repetición idempotente no lo repite. Sin correlación pedida (alta manual) no hay aviso.
    /// </summary>
    [Fact]
    public async Task Attribution_notice_is_published_in_the_same_unit()
    {
        const string plaintextCode = "FRIEND-2026";
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attributions = new InMemoryReferralAttributionRepository();
        var idempotency = new FakeReferralIdempotencyExecutor(attributions);
        var command = new CreateReferralAttributionCommand(
            GrowthTestData.RefereeTenantId,
            program.Id,
            plaintextCode,
            ReferralParticipantType.Tenant,
            GrowthTestData.RefereeTenantId,
            "attribution-notice",
            GrowthTestData.ActorId,
            NoticeCorrelationId: "corr-attribution"
        );

        Task<(Result<CreateReferralAttributionResult> Result, object? Notice)> Run(
            CreateReferralAttributionCommand toRun
        ) =>
            CreateReferralAttributionHandler.Handle(
                toRun,
                new InMemoryReferralProgramRepository(program),
                new InMemoryReferralCodeRepository(code),
                attributions,
                new FakeReferralCodeTokenHasher(plaintextCode, code.CodeHash),
                idempotency,
                new NoShares(),
                Events,
                new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddMinutes(1))),
                CancellationToken.None
            );

        var first = await Run(command);
        var replay = await Run(command);
        var manual = await Run(command with { IdempotencyKey = "attribution-manual", NoticeCorrelationId = null });

        var notice = Assert.IsType<ReferralAttributedIntegrationEvent>(first.Notice);
        Assert.Equal(first.Result.Value.AttributionId, notice.AttributionId);
        Assert.Equal(code.OwnerId, notice.TenantId);
        Assert.True(replay.Result.IsSuccess);
        Assert.Null(replay.Notice);
        Assert.True(manual.Result.IsSuccess);
        Assert.Null(manual.Notice);
    }

    private sealed class NoShares : TaxVision.Referrals.Application.Abstractions.IReferralCodeShareRepository
    {
        public Task AddAsync(
            TaxVision.Referrals.Domain.Shares.ReferralCodeShare share,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<Guid?> FindReferralCodeIdAsync(Guid shareId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);
    }
}
