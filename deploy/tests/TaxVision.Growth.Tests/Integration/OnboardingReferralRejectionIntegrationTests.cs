using BuildingBlocks.Tenancy;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Referrals.Application.Attributions.OnboardingReferralRejections;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server real (base temporal): el rechazo de la declaración de referido se anota una sola vez por alta,
/// aunque la declaración llegue dos veces, y solo lo ve el propio alta.
/// </summary>
public sealed class OnboardingReferralRejectionIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_rejection_is_recorded_once_and_only_the_onboarding_sees_it()
    {
        var onboardingId = Guid.NewGuid();

        for (var delivery = 0; delivery < 2; delivery++)
        {
            await using var db = _database.NewContext(onboardingId);
            var recorded = await RecordOnboardingReferralRejectionHandler.Handle(
                new RecordOnboardingReferralRejectionCommand(onboardingId, "Growth.Attribution.CodeNotFound"),
                Repository(db, onboardingId),
                db,
                TimeProvider.System,
                CancellationToken.None
            );
            Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Error.Code : null);
        }

        await using (var db = _database.NewContext(onboardingId))
        {
            var seen = await HasOnboardingReferralRejectionHandler.Handle(
                new HasOnboardingReferralRejectionQuery(onboardingId),
                Repository(db, onboardingId),
                CancellationToken.None
            );
            Assert.True(seen.Value);
            Assert.Equal(1, db.OnboardingReferralRejections.Count(rejection => rejection.OnboardingId == onboardingId));
        }

        var otherTenant = Guid.NewGuid();
        await using (var db = _database.NewContext(otherTenant))
        {
            var seen = await HasOnboardingReferralRejectionHandler.Handle(
                new HasOnboardingReferralRejectionQuery(onboardingId),
                Repository(db, otherTenant),
                CancellationToken.None
            );
            Assert.False(seen.Value);
        }
    }

    private static OnboardingReferralRejectionRepository Repository(GrowthDbContext db, Guid tenantId) =>
        new(db, new FixedTenant(tenantId));

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
