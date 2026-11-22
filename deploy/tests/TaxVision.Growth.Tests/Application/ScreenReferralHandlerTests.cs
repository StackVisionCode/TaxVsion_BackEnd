using Microsoft.Extensions.Options;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.SharedKernel.Buyers;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Al enlazar la oficina referida (o al conocerse el email de su dueño), Growth la compara con el referidor: el mismo
/// usuario pierde el premio solo; el mismo email normalizado o uno desechable retienen el premio en revisión.
/// </summary>
public sealed class ScreenReferralHandlerTests
{
    private static readonly Guid ReferrerUser = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = Guid.Parse("50000000-0000-0000-0000-000000000002");

    private static readonly HmacBuyerIdentityHasher Hasher = new(
        Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })
    );

    private static readonly TaxVision.Growth.Api.IntegrationEvents.ReferralNoticeEvents Events = new();

    /// <summary>
    /// El aviso de premio cancelado vuelve como mensaje en cascada y sale por el outbox de la misma transacción que
    /// el rechazo; repetir el screening (atribución ya rechazada) no lo repite. Una revisión abierta no avisa.
    /// </summary>
    [Fact]
    public async Task Rejected_notice_is_published_in_the_same_unit()
    {
        var f = new Fixture();
        f.Link(ReferrerUser);

        var first = await f.RunWithNoticeAsync();
        var again = await f.RunWithNoticeAsync();

        var notice =
            Assert.IsType<BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralRewardRejectedIntegrationEvent>(
                first.Notice
            );
        Assert.Equal("SelfReferral", notice.ReasonCode);
        Assert.Equal(f.Reward.Id, notice.RewardCaseId);
        Assert.Equal(GrowthTestData.ReferrerTenantId, notice.TenantId);
        Assert.Equal("corr-screen", notice.CorrelationId);
        Assert.Null(again.Notice);
    }

    [Fact]
    public async Task A_review_hold_does_not_notify()
    {
        var f = new Fixture();
        f.Owners.Signals[f.Office] = new OfficeOwnerSignals("v1:referee", OwnerEmailDisposable: true);
        f.Link(OtherUser);

        var held = await f.RunWithNoticeAsync();

        Assert.Equal(ReferralFraudVerdict.Review, held.Result.Value.Verdict);
        Assert.Null(held.Notice);
    }

    [Fact]
    public async Task A_signup_by_the_referrer_user_loses_the_reward()
    {
        var f = new Fixture();
        f.Link(ReferrerUser);

        var screened = await f.RunAsync();

        Assert.Equal(ReferralFraudVerdict.Block, screened.Value.Verdict);
        Assert.True(screened.Value.RewardCancelled);
        Assert.Equal(ReferralAttributionStatus.Rejected, f.Attribution.Status);
        Assert.Equal(ReferralRewardCaseStatus.Cancelled, f.Reward.Status);
        var review = Assert.Single(f.Reviews.Items);
        Assert.Equal(FraudReviewStatus.Rejected, review.Status);
        Assert.Equal(ReferralFraudScreen.SameUser, review.SignalCode);
    }

    [Fact]
    public async Task Self_referral_with_an_email_alias_opens_a_review()
    {
        var f = new Fixture();
        f.Owners.Signals[f.Office] = Owner("A.na+second@gmail.com");
        f.Owners.Signals[GrowthTestData.ReferrerTenantId] = Owner("ana@gmail.com");
        f.Link(OtherUser);

        var screened = await f.RunAsync();

        Assert.Equal(ReferralFraudVerdict.Review, screened.Value.Verdict);
        Assert.Equal(ReferralAttributionStatus.UnderReview, f.Attribution.Status);
        Assert.Equal(ReferralRewardCaseStatus.ManualReview, f.Reward.Status);
        var review = Assert.Single(f.Reviews.Items);
        Assert.Equal(FraudReviewStatus.Open, review.Status);
        Assert.Equal(ReferralFraudScreen.SameOwnerEmail, review.SignalCode);
        Assert.Equal(f.Attribution.Id, review.AttributionId);
        Assert.Equal(f.Reward.Id, review.RewardCaseId);
        Assert.Equal(GrowthTestData.ReferrerTenantId, review.TenantId);
        Assert.DoesNotContain("@", review.EvidenceReference);
    }

    [Fact]
    public async Task The_worker_does_not_pay_a_reward_under_review()
    {
        var f = new Fixture();
        f.Owners.Signals[f.Office] = new OfficeOwnerSignals("v1:referee", OwnerEmailDisposable: true);
        f.Link(OtherUser);

        await f.RunAsync();

        var payable = await f.Rewards.GetGrantableWalletCreditCasesAsync(GrowthTestData.NowUtc.AddYears(1), 100);
        Assert.Empty(payable);
    }

    [Fact]
    public async Task Screening_again_keeps_a_single_review()
    {
        var f = new Fixture();
        f.Owners.Signals[f.Office] = Owner("ana@gmail.com");
        f.Owners.Signals[GrowthTestData.ReferrerTenantId] = Owner("ana@gmail.com");
        f.Link(OtherUser);

        await f.RunAsync();
        var again = await f.RunAsync();

        Assert.True(again.IsSuccess);
        Assert.Single(f.Reviews.Items);
    }

    [Fact]
    public async Task Different_owners_keep_the_reward_waiting()
    {
        var f = new Fixture();
        f.Owners.Signals[f.Office] = Owner("maria@example.com");
        f.Owners.Signals[GrowthTestData.ReferrerTenantId] = Owner("ana@example.com");
        f.Link(OtherUser);

        var screened = await f.RunAsync();

        Assert.Equal(ReferralFraudVerdict.Clear, screened.Value.Verdict);
        Assert.Equal(ReferralRewardCaseStatus.Requested, f.Reward.Status);
        Assert.Empty(f.Reviews.Items);
    }

    [Fact]
    public async Task Before_the_office_is_linked_nothing_is_decided()
    {
        var f = new Fixture();

        var screened = await f.RunAsync();

        Assert.Equal(ReferralFraudVerdict.Clear, screened.Value.Verdict);
        Assert.Equal(0, f.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_signup_without_a_referral_is_ignored()
    {
        var f = new Fixture();

        var (screened, _) = await ScreenReferralHandler.Handle(
            new ScreenReferralCommand(Guid.NewGuid(), "corr-screen"),
            f.Attributions,
            f.Rewards,
            f.Reviews,
            f.Owners,
            f.Tenant,
            f.UnitOfWork,
            Events,
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal(ReferralFraudVerdict.Clear, screened.Value.Verdict);
    }

    private static OfficeOwnerSignals Owner(string email) =>
        new(Hasher.Hash(NormalizedEmail.Create(email).Value).SubjectId, OwnerEmailDisposable: false);

    internal sealed class Fixture
    {
        public Guid Onboarding { get; } = Guid.NewGuid();
        public Guid Office { get; } = Guid.NewGuid();
        public ReferralAttribution Attribution { get; }
        public ReferralRewardCase Reward { get; }
        public InMemoryReferralAttributionRepository Attributions { get; }
        public InMemoryReferralRewardCaseRepository Rewards { get; }
        public InMemoryReferralFraudReviewRepository Reviews { get; } = new();
        public FakeOfficeOwnerSignals Owners { get; } = new();
        public ClawbackWalletCreditRewardHandlerTests.RecordingTenantContext Tenant { get; } = new();
        public GrantWalletCreditRewardHandlerTests.CountingUnitOfWork UnitOfWork { get; } = new();

        public Fixture()
        {
            var program = GrowthTestData.CreateActiveTenantReferralProgram();
            var code = GrowthTestData.CreateReferralCode(program, ownerUserId: ReferrerUser);
            Attribution = GrowthTestData.CreateActiveAttribution(program, code, Onboarding);
            var qualification = GrowthTestData.CreateQualifiedReferral(program, Attribution);
            Assert.True(Attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
            Reward = ReferralRewardCase
                .Request(
                    program,
                    Attribution,
                    qualification,
                    "reward-screen",
                    GrowthTestData.Sha('8'),
                    GrowthTestData.ActorId,
                    GrowthTestData.NowUtc
                )
                .Value;
            Attributions = new InMemoryReferralAttributionRepository(Attribution);
            Rewards = new InMemoryReferralRewardCaseRepository(Reward);
        }

        public void Link(Guid refereeUser) =>
            Assert.True(Attribution.LinkRefereeTenant(Office, GrowthTestData.NowUtc, refereeUser).IsSuccess);

        public async Task<BuildingBlocks.Results.Result<ReferralScreeningResult>> RunAsync() =>
            (await RunWithNoticeAsync()).Result;

        public Task<(
            BuildingBlocks.Results.Result<ReferralScreeningResult> Result,
            object? Notice
        )> RunWithNoticeAsync() =>
            ScreenReferralHandler.Handle(
                new ScreenReferralCommand(Onboarding, "corr-screen"),
                Attributions,
                Rewards,
                Reviews,
                Owners,
                Tenant,
                UnitOfWork,
                Events,
                new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddMinutes(5))),
                CancellationToken.None
            );
    }

    internal sealed class FakeOfficeOwnerSignals : IOfficeOwnerSignalsReader
    {
        public Dictionary<Guid, OfficeOwnerSignals> Signals { get; } = [];

        public Task<OfficeOwnerSignals?> ForOfficeAsync(Guid officeTenantId, CancellationToken ct = default) =>
            Task.FromResult(Signals.GetValueOrDefault(officeTenantId));
    }
}
