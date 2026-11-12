using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// REF-3 (por evento, sin M2M): el premio del referidor se PIDE al Wallet con una solicitud genérica y, cuando el Wallet
/// responde, el caso queda <c>Granted</c> con su <see cref="ReferrerBenefit"/>. Resumible e idempotente.
/// </summary>
public sealed class GrantWalletCreditRewardHandlerTests
{
    // El premio parte de un pago de $25.00 USD: 10 % = 250 centavos; espera de 30 días.
    private static readonly DateTime AfterWait = GrowthTestData.NowUtc.AddDays(1).AddDays(31);

    [Fact]
    public async Task It_requests_the_credit_from_the_wallet_and_leaves_the_case_pending_until_the_wallet_answers()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(AfterWait);

        Assert.True(result.IsSuccess);
        Assert.Equal(GrantWalletCreditRewardOutcome.Requested, result.Value.Outcome);
        Assert.Equal(ReferralRewardCaseStatus.PendingGrant, f.Reward.Status);
        var request = Assert.Single(f.Wallet.Requests);
        Assert.Equal(GrowthTestData.ReferrerTenantId, request.BeneficiaryTenantId);
        Assert.Equal(f.Reward.Id, request.RewardCaseId);
        Assert.Equal(250, request.AmountCents);
        Assert.Equal("USD", request.Currency);
        Assert.False(request.Reverse);
        Assert.Equal(ReferralRewardAttemptStatus.Pending, Assert.Single(f.Attempts.Items).Status);
        Assert.Empty(f.Benefits.Items);
    }

    [Fact]
    public async Task Confirming_after_the_wallet_answers_grants_the_case_and_records_the_benefit()
    {
        var f = Fixture.Create();
        await f.RunAsync(AfterWait);
        var ledgerEntry = Guid.NewGuid();

        var confirmed = await f.ConfirmAsync(ledgerEntry, AfterWait.AddSeconds(5));

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(ConfirmWalletCreditRewardOutcome.Granted, confirmed.Value);
        Assert.Equal(ReferralRewardCaseStatus.Granted, f.Reward.Status);
        Assert.Equal($"wallet-credit:{ledgerEntry:N}", f.Reward.MaterializedBenefitReference);
        var benefit = Assert.Single(f.Benefits.Items);
        Assert.Equal(f.Reward.Id, benefit.RewardCaseId);
        Assert.Equal(250, benefit.TotalBenefitWalletCents);
        Assert.Equal(ReferralRewardAttemptStatus.Succeeded, Assert.Single(f.Attempts.Items).Status);
    }

    [Fact]
    public async Task A_redelivered_confirmation_is_a_no_op_and_never_records_the_benefit_twice()
    {
        var f = Fixture.Create();
        await f.RunAsync(AfterWait);
        var ledgerEntry = Guid.NewGuid();
        await f.ConfirmAsync(ledgerEntry, AfterWait.AddSeconds(5));

        var again = await f.ConfirmAsync(ledgerEntry, AfterWait.AddSeconds(9));

        Assert.True(again.IsSuccess);
        Assert.Equal(ConfirmWalletCreditRewardOutcome.AlreadyGranted, again.Value);
        Assert.Single(f.Benefits.Items);
    }

    [Fact]
    public async Task Nothing_is_requested_before_the_waiting_period_elapses()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(GrowthTestData.NowUtc.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralReward.WaitingPeriod", result.Error.Code);
        Assert.Equal(ReferralRewardCaseStatus.Requested, f.Reward.Status);
        Assert.Empty(f.Wallet.Requests);
    }

    [Fact]
    public async Task A_pending_case_without_an_answer_is_asked_again_without_a_second_attempt()
    {
        var f = Fixture.Create();
        await f.RunAsync(AfterWait);

        var retry = await f.RunAsync(AfterWait.AddMinutes(10));

        Assert.True(retry.IsSuccess);
        Assert.Equal(GrantWalletCreditRewardOutcome.Requested, retry.Value.Outcome);
        Assert.Equal(2, f.Wallet.Requests.Count);
        Assert.Single(f.Attempts.Items);
        Assert.Equal(AfterWait.AddMinutes(10), f.Reward.UpdatedAtUtc);
    }

    [Fact]
    public async Task The_request_is_returned_only_after_the_state_is_saved()
    {
        var f = Fixture.Create();

        var result = await f.RunAsync(AfterWait);

        // Si quien publica falla, el caso ya quedó PendingGrant y el worker lo reintenta.
        Assert.Equal(1, f.UnitOfWork.SaveCount);
        Assert.NotNull(result.Value.Request);
        Assert.Equal(f.Reward.Id, result.Value.Request!.RewardCaseId);
    }

    [Fact]
    public async Task Running_again_after_it_was_granted_is_a_no_op_and_never_requests_twice()
    {
        var f = Fixture.Create();
        await f.RunAsync(AfterWait);
        await f.ConfirmAsync(Guid.NewGuid(), AfterWait.AddSeconds(5));

        var again = await f.RunAsync(AfterWait.AddHours(1));

        Assert.True(again.IsSuccess);
        Assert.Equal(GrantWalletCreditRewardOutcome.AlreadyGranted, again.Value.Outcome);
        Assert.Single(f.Wallet.Requests);
    }

    [Fact]
    public async Task A_non_wallet_credit_reward_is_rejected()
    {
        var policy = ReferralProgramPolicy
            .CreateTenantToTenant(90, 1, null, 30, 10, ReferralRewardType.SubscriptionTrialExtension, "referrals.trial")
            .Value;
        var f = Fixture.Create(policy);

        var result = await f.RunAsync(AfterWait);

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralReward.NotWalletCredit", result.Error.Code);
        Assert.Empty(f.Wallet.Requests);
    }

    [Fact]
    public async Task An_unknown_case_is_not_found()
    {
        var f = Fixture.Create();

        var result = await GrantWalletCreditRewardHandler.Handle(
            new GrantWalletCreditRewardCommand(GrowthTestData.ReferrerTenantId, Guid.NewGuid(), GrowthTestData.ActorId),
            f.Rewards,
            f.Attempts,
            f.UnitOfWork,
            new FixedTimeProvider(new DateTimeOffset(AfterWait)),
            NullLogger<GrantWalletCreditRewardCommand>.Instance,
            CancellationToken.None
        );

        Assert.Equal("ReferralReward.NotFound", result.Error.Code);
    }

    private sealed class Fixture
    {
        public required ReferralRewardCase Reward { get; init; }
        public required InMemoryReferralRewardCaseRepository Rewards { get; init; }
        public InMemoryAttemptRepository Attempts { get; } = new();
        public InMemoryBenefitRepository Benefits { get; } = new();
        public FakeWalletCreditRequests Wallet { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; } = new();

        public static Fixture Create(ReferralProgramPolicy? policy = null)
        {
            var program = policy is null ? GrowthTestData.CreateActiveTenantReferralProgram() : ProgramWith(policy);
            var attribution = GrowthTestData.CreateActiveAttribution(
                program,
                GrowthTestData.CreateReferralCode(program)
            );
            var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
            Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);
            var reward = ReferralRewardCase
                .Request(
                    program,
                    attribution,
                    qualification,
                    "reward-handler",
                    GrowthTestData.Sha('9'),
                    GrowthTestData.ActorId,
                    GrowthTestData.NowUtc.AddDays(1)
                )
                .Value;
            return new Fixture { Reward = reward, Rewards = new InMemoryReferralRewardCaseRepository(reward) };
        }

        public async Task<Result<GrantWalletCreditRewardResult>> RunAsync(DateTime nowUtc)
        {
            var result = await GrantWalletCreditRewardHandler.Handle(
                new GrantWalletCreditRewardCommand(Reward.TenantId, Reward.Id, GrowthTestData.ActorId),
                Rewards,
                Attempts,
                UnitOfWork,
                new FixedTimeProvider(new DateTimeOffset(nowUtc)),
                NullLogger<GrantWalletCreditRewardCommand>.Instance,
                CancellationToken.None
            );
            if (result.IsSuccess && result.Value.Request is { } request)
                Wallet.Requests.Add(request);
            return result;
        }

        public Task<Result<ConfirmWalletCreditRewardOutcome>> ConfirmAsync(Guid ledgerEntryId, DateTime nowUtc) =>
            ConfirmWalletCreditRewardHandler.Handle(
                new ConfirmWalletCreditRewardCommand(Reward.TenantId, Reward.Id, ledgerEntryId, GrowthTestData.ActorId),
                Rewards,
                Attempts,
                Benefits,
                UnitOfWork,
                new FixedTimeProvider(new DateTimeOffset(nowUtc)),
                NullLogger<ConfirmWalletCreditRewardCommand>.Instance,
                CancellationToken.None
            );

        private static ReferralProgram ProgramWith(ReferralProgramPolicy policy)
        {
            var program = ReferralProgram
                .Create(
                    "T2T-NONMONETARY",
                    "Non monetary",
                    ReferralProgramScope.Platform,
                    tenantScopeId: null,
                    ReferralFlowType.TenantToTenant,
                    policy,
                    GrowthTestData.NowUtc.AddDays(-1),
                    GrowthTestData.NowUtc.AddYears(1),
                    "program-nonmonetary",
                    GrowthTestData.Sha('1'),
                    GrowthTestData.ActorId,
                    GrowthTestData.NowUtc
                )
                .Value;
            Assert.True(program.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
            return program;
        }
    }

    internal sealed class InMemoryAttemptRepository : IReferralRewardAttemptRepository
    {
        private readonly List<ReferralRewardAttempt> _items = [];
        public IReadOnlyList<ReferralRewardAttempt> Items => _items;

        public Task<ReferralRewardAttempt?> GetByIdAsync(
            Guid attemptId,
            Guid ownerTenantId,
            CancellationToken ct = default
        ) => Task.FromResult(_items.FirstOrDefault(a => a.Id == attemptId && a.TenantId == ownerTenantId));

        public Task<ReferralRewardAttempt?> GetOpenGrantAttemptAsync(
            Guid rewardCaseId,
            Guid ownerTenantId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _items.FirstOrDefault(a =>
                    a.RewardCaseId == rewardCaseId
                    && a.TenantId == ownerTenantId
                    && a.Operation == ReferralRewardOperation.Grant
                    && a.Status == ReferralRewardAttemptStatus.Pending
                )
            );

        public Task AddAsync(ReferralRewardAttempt attempt, CancellationToken ct = default)
        {
            _items.Add(attempt);
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemoryBenefitRepository : IReferrerBenefitRepository
    {
        private readonly List<ReferrerBenefit> _items = [];
        public IReadOnlyList<ReferrerBenefit> Items => _items;

        public Task<ReferrerBenefit?> GetByRewardCaseAsync(
            Guid rewardCaseId,
            Guid ownerTenantId,
            CancellationToken ct = default
        ) => Task.FromResult(_items.FirstOrDefault(b => b.RewardCaseId == rewardCaseId && b.TenantId == ownerTenantId));

        public Task AddAsync(ReferrerBenefit benefit, CancellationToken ct = default)
        {
            _items.Add(benefit);
            return Task.CompletedTask;
        }
    }

    /// <summary>Recoge las solicitudes de crédito/reversa que los casos de uso devuelven (el worker/consumer las publicaría).</summary>
    internal sealed class FakeWalletCreditRequests
    {
        public List<WalletCreditRequest> Requests { get; } = [];
    }

    internal sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
