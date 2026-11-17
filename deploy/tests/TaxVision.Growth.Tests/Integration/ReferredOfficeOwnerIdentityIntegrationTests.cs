using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Infrastructure.Persistence.Referrals;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.SharedKernel.Buyers;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server (base temporal): al crearse una oficina, Growth guarda solo el HMAC del email normalizado de su
/// dueño y si el dominio es desechable. El email en claro no queda en ninguna columna.
/// </summary>
public sealed class ReferredOfficeOwnerIdentityIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    private static readonly HmacBuyerIdentityHasher Hasher = new(
        Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })
    );

    private ScriptedBus Bus { get; } = new(_ => BuildingBlocks.Results.Result.Success(ReferralScreeningResult.Clear));

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_owner_email_is_kept_only_as_its_normalized_hash()
    {
        var office = Guid.NewGuid();

        await HandleAsync(Created(office, "Ana.Perez+office2@googlemail.com"));

        var stored = await OfficeAsync(office);
        var expected = Hasher.Hash(NormalizedEmail.Create("anaperez@gmail.com").Value).SubjectId;
        Assert.Equal(expected, stored.OwnerSubjectId);
        Assert.False(stored.OwnerEmailDisposable);
        Assert.DoesNotContain("@", stored.OwnerSubjectId);
    }

    [Fact]
    public async Task A_signup_office_is_screened_under_its_onboarding_tenant()
    {
        var created = Created(Guid.NewGuid(), "owner@example.com");

        await HandleAsync(created);

        Assert.Equal(
            created.OnboardingId,
            Assert.IsType<ScreenReferralCommand>(Assert.Single(Bus.Invoked)).OnboardingId
        );
        Assert.Equal(created.OnboardingId.ToString(), Bus.TenantIdDuring[0]);
        Assert.Null(Bus.TenantId);
    }

    [Fact]
    public async Task A_disposable_owner_email_is_marked()
    {
        var office = Guid.NewGuid();

        await HandleAsync(Created(office, "owner@mailinator.com"));

        Assert.True((await OfficeAsync(office)).OwnerEmailDisposable);
    }

    [Fact]
    public async Task An_unreadable_email_leaves_no_identity()
    {
        var office = Guid.NewGuid();

        await HandleAsync(Created(office, "not-an-email"));

        var stored = await OfficeAsync(office);
        Assert.Null(stored.OwnerSubjectId);
        Assert.False(stored.OwnerEmailDisposable);
    }

    private static TenantCreatedIntegrationEvent Created(Guid office, string adminEmail) =>
        new()
        {
            NewTenantId = office,
            Name = "Owner Identity Office",
            SubDomain = "owneridentity",
            AdminEmail = adminEmail,
            AdminInvitationTokenHash = string.Empty,
            OnboardingId = Guid.NewGuid(),
            OccurredOn = DateTime.UtcNow,
        };

    private async Task HandleAsync(TenantCreatedIntegrationEvent created)
    {
        await using var db = _database.NewContext(Guid.Empty);
        await ReferredOfficeProjectionConsumer.Handle(
            created,
            new ReferredOfficeProjectionStore(db),
            Hasher,
            new FileDisposableEmailDomains(),
            db,
            Bus,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantCreatedIntegrationEvent>.Instance,
            CancellationToken.None
        );
    }

    private async Task<ReferredOfficeProjection> OfficeAsync(Guid office)
    {
        await using var db = _database.NewContext(office);
        return await db.ReferredOffices.IgnoreQueryFilters().SingleAsync(item => item.TenantId == office);
    }
}
