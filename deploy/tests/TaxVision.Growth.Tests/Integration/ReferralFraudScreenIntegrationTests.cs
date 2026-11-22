using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Attributions.LinkReferralRefereeTenant;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server (base temporal): el alta referida por una oficina con el mismo email de dueño queda enlazada, su
/// premio retenido y la revisión guardada (con la atribución, el premio y el programa).
/// </summary>
public sealed class ReferralFraudScreenIntegrationTests : IAsyncLifetime
{
    private static readonly Guid ReferrerUser = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private readonly GrowthTemporaryDatabase _database = new();
    private readonly Guid _onboarding = Guid.NewGuid();
    private readonly Guid _office = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Same_owner_email_holds_the_reward_and_stores_the_review()
    {
        var rewardId = await SeedAsync(sameOwner: "v1:same", refereeOwner: "v1:same");

        await LinkAndScreenAsync(refereeUser: Guid.NewGuid());

        await using var db = _database.NewContext(Guid.Empty);
        var review = await db.ReferralFraudReviews.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(FraudReviewStatus.Open, review.Status);
        Assert.Equal(ReferralFraudScreen.SameOwnerEmail, review.SignalCode);
        Assert.Equal(rewardId, review.RewardCaseId);
        var reward = await db.ReferralRewardCases.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ReferralRewardCaseStatus.ManualReview, reward.Status);
        var attribution = await db.ReferralAttributions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ReferralAttributionStatus.UnderReview, attribution.Status);

        // La plataforma ve la revisión abierta en su cola.
        await using var admin = _database.NewContext(PlatformTenant.Id);
        var page = await new ReferralAdminReadModel(admin, new FixedTenant(PlatformTenant.Id)).ListFraudReviewsAsync(
            FraudReviewStatus.Open,
            1,
            20
        );
        var row = Assert.Single(page.Items);
        Assert.Equal(review.Id, row.ReviewId);
        Assert.Equal(GrowthTestData.ReferrerTenantId, row.ReferrerTenantId);
        Assert.Equal(ReferralFraudScreen.SameOwnerEmail, row.SignalCode);
    }

    [Fact]
    public async Task The_referrer_user_signing_up_loses_the_reward()
    {
        await SeedAsync(sameOwner: "v1:referrer", refereeOwner: "v1:referee");

        await LinkAndScreenAsync(refereeUser: ReferrerUser);

        await using var db = _database.NewContext(Guid.Empty);
        var attribution = await db.ReferralAttributions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ReferralAttributionStatus.Rejected, attribution.Status);
        Assert.Equal(ReferrerUser, attribution.RefereeUserId);
        Assert.Equal(
            ReferralRewardCaseStatus.Cancelled,
            (await db.ReferralRewardCases.IgnoreQueryFilters().SingleAsync()).Status
        );
        Assert.Equal(
            FraudReviewStatus.Rejected,
            (await db.ReferralFraudReviews.IgnoreQueryFilters().SingleAsync()).Status
        );
    }

    private async Task<Guid> SeedAsync(string sameOwner, string refereeOwner)
    {
        await using var seed = _database.NewContext(Guid.Empty);
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program, ownerUserId: ReferrerUser);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code, _onboarding);
        var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-screen",
                GrowthTestData.Sha('8'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;
        seed.AddRange(program, code, attribution, qualification, reward);
        seed.ReferredOffices.Add(Office(GrowthTestData.ReferrerTenantId, sameOwner));
        seed.ReferredOffices.Add(Office(_office, refereeOwner));
        await seed.SaveChangesAsync();
        return reward.Id;
    }

    private static ReferredOfficeProjection Office(Guid tenant, string owner)
    {
        var office = ReferredOfficeProjection.Create(tenant);
        office.AttachOwner(owner, disposable: false);
        return office;
    }

    private async Task LinkAndScreenAsync(Guid refereeUser)
    {
        await using (var db = _database.NewContext(_onboarding))
        {
            var linked = await LinkReferralRefereeTenantHandler.Handle(
                new LinkReferralRefereeTenantCommand(_onboarding, _office, refereeUser),
                new ReferralAttributionRepository(db, new FixedTenant(_onboarding)),
                db,
                TimeProvider.System,
                CancellationToken.None
            );
            Assert.True(linked.IsSuccess, linked.IsFailure ? linked.Error.Code : null);
        }

        var tenant = new SwitchableTenant();
        await using var screenDb = _database.NewContext(tenant);
        var (screened, _) = await ScreenReferralHandler.Handle(
            new ScreenReferralCommand(_onboarding, "corr-screen"),
            new ReferralAttributionRepository(screenDb, tenant),
            new ReferralRewardCaseRepository(screenDb, tenant),
            new ReferralFraudReviewRepository(screenDb, tenant),
            new OfficeOwnerSignalsReader(screenDb),
            tenant,
            screenDb,
            new TaxVision.Growth.Api.IntegrationEvents.ReferralNoticeEvents(),
            TimeProvider.System,
            CancellationToken.None
        );
        Assert.True(screened.IsSuccess, screened.IsFailure ? screened.Error.Code : null);
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class SwitchableTenant : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool HasTenant => TenantId != Guid.Empty;

        public void SetTenant(Guid id) => TenantId = id;
    }
}
