using BuildingBlocks.Tenancy;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Attributions.LinkReferralRefereeTenant;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server real (base temporal): la atribución del alta se encuentra por su <c>OnboardingId</c>, queda
/// enlazada con la oficina real y la lista del referidor muestra esa oficina (también por su tenant).
/// </summary>
public sealed class ReferralRefereeTenantLinkIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_completed_onboarding_links_its_attribution_to_the_real_office()
    {
        var onboardingId = Guid.NewGuid();
        var office = Guid.NewGuid();
        await using (var seed = _database.NewContext(Guid.Empty))
        {
            var program = GrowthTestData.CreateActiveTenantReferralProgram();
            var code = GrowthTestData.CreateReferralCode(program);
            seed.AddRange(program, code, GrowthTestData.CreateActiveAttribution(program, code, onboardingId));
            // Oficina conocida solo por su tenant (sin alta): el nombre sale por el enlace.
            var projection = ReferredOfficeProjection.Create(office);
            projection.ApplyName("Castillo Tax", DateTime.UtcNow);
            seed.ReferredOffices.Add(projection);
            await seed.SaveChangesAsync();
        }

        await using (var db = _database.NewContext(onboardingId))
        {
            var linked = await LinkReferralRefereeTenantHandler.Handle(
                new LinkReferralRefereeTenantCommand(onboardingId, office),
                new ReferralAttributionRepository(db, new FixedTenant(onboardingId)),
                db,
                TimeProvider.System,
                CancellationToken.None
            );
            Assert.True(linked.IsSuccess, linked.IsFailure ? linked.Error.Code : null);
        }

        await using var reader = _database.NewContext(GrowthTestData.ReferrerTenantId);
        var page = await new ReferralReadModel(
            reader,
            new FixedTenant(GrowthTestData.ReferrerTenantId)
        ).ListInvitesAsync(GrowthTestData.ReferrerTenantId, 1, 20);
        var row = Assert.Single(page.Items);
        Assert.Equal(office, row.RefereeTenantId);
        Assert.Equal("Castillo Tax", row.RefereeName);
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
