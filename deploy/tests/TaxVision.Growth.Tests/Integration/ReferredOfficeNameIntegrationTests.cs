using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server real (base temporal): la lista de referidos del referidor muestra el nombre real de la oficina
/// referida, que Growth toma de los eventos de Tenant (creación, renombre y perfil), sin aceptar eventos viejos.
/// </summary>
public sealed class ReferredOfficeNameIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_referrer_sees_the_referred_office_name_once_the_signup_creates_it()
    {
        var onboardingId = Guid.NewGuid();
        var officeTenantId = Guid.NewGuid();
        await SeedAttributionAsync(onboardingId);

        Assert.Null(await RefereeNameAsync());

        var createdAt = DateTime.UtcNow;
        await HandleAsync(
            new TenantCreatedIntegrationEvent
            {
                NewTenantId = officeTenantId,
                Name = "Castillo Tax",
                SubDomain = "castillotax",
                AdminEmail = "owner@castillotax.test",
                AdminInvitationTokenHash = string.Empty,
                OnboardingId = onboardingId,
                OccurredOn = createdAt,
            }
        );
        Assert.Equal("Castillo Tax", await RefereeNameAsync());

        // El nombre comercial manda sobre el nombre de la oficina.
        await HandleAsync(
            new TenantProfileChangedIntegrationEvent
            {
                ProfileTenantId = officeTenantId,
                Version = 2,
                UpdatedAtUtc = createdAt.AddMinutes(2),
                DisplayName = "Castillo Tax & Co",
            }
        );
        Assert.Equal("Castillo Tax & Co", await RefereeNameAsync());

        // Un perfil o un renombre más viejos que lo guardado no pisan nada.
        await HandleAsync(
            new TenantProfileChangedIntegrationEvent
            {
                ProfileTenantId = officeTenantId,
                Version = 1,
                UpdatedAtUtc = createdAt.AddMinutes(1),
                DisplayName = "Old name",
            }
        );
        await HandleAsync(
            new TenantUpdatedIntegrationEvent
            {
                UpdatedTenantId = officeTenantId,
                Name = "Castillo Older",
                DefaultTimeZoneId = "UTC",
                UpdatedAtUtc = createdAt.AddMinutes(-5),
            }
        );
        Assert.Equal("Castillo Tax & Co", await RefereeNameAsync());

        // Sin nombre comercial, vale el nombre de la oficina más reciente.
        await HandleAsync(
            new TenantProfileChangedIntegrationEvent
            {
                ProfileTenantId = officeTenantId,
                Version = 3,
                UpdatedAtUtc = createdAt.AddMinutes(3),
                DisplayName = null,
            }
        );
        await HandleAsync(
            new TenantUpdatedIntegrationEvent
            {
                UpdatedTenantId = officeTenantId,
                Name = "Castillo Tax Group",
                DefaultTimeZoneId = "UTC",
                UpdatedAtUtc = createdAt.AddMinutes(4),
            }
        );
        Assert.Equal("Castillo Tax Group", await RefereeNameAsync());
    }

    private async Task SeedAttributionAsync(Guid onboardingId)
    {
        await using var db = _database.NewContext(Guid.Empty);
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code, onboardingId);
        db.AddRange(program, code, attribution);
        await db.SaveChangesAsync();
    }

    private async Task HandleAsync(object message)
    {
        await using var db = _database.NewContext(Guid.Empty);
        var store = new ReferredOfficeProjectionStore(db);
        switch (message)
        {
            case TenantCreatedIntegrationEvent created:
                await ReferredOfficeProjectionConsumer.Handle(
                    created,
                    store,
                    new HmacBuyerIdentityHasher(
                        Microsoft.Extensions.Options.Options.Create(
                            new CodeTokenHashingOptions { Pepper = new string('p', 48) }
                        )
                    ),
                    new FileDisposableEmailDomains(),
                    db,
                    new TaxVision.Growth.Tests.Application.Fakes.ScriptedBus(_ =>
                        BuildingBlocks.Results.Result.Success(
                            TaxVision.Referrals.Application.Fraud.ScreenReferral.ReferralScreeningResult.Clear
                        )
                    ),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantCreatedIntegrationEvent>.Instance,
                    CancellationToken.None
                );
                break;
            case TenantUpdatedIntegrationEvent updated:
                await ReferredOfficeProjectionConsumer.Handle(updated, store, db, CancellationToken.None);
                break;
            case TenantProfileChangedIntegrationEvent profile:
                await ReferredOfficeProjectionConsumer.Handle(profile, store, db, CancellationToken.None);
                break;
        }
    }

    private async Task<string?> RefereeNameAsync()
    {
        await using var db = _database.NewContext(GrowthTestData.ReferrerTenantId);
        var page = await new ReferralReadModel(db, new FixedTenant(GrowthTestData.ReferrerTenantId)).ListInvitesAsync(
            GrowthTestData.ReferrerTenantId,
            1,
            20
        );
        return Assert.Single(page.Items).RefereeName;
    }

    private sealed class FixedTenant(Guid tenantId) : BuildingBlocks.Tenancy.ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
