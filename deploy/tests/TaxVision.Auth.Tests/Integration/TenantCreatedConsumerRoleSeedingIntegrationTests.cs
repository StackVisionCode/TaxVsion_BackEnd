using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Tenants.IntegrationEvents;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;
using TaxVision.Auth.Tests.Onboarding;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Roles de sistema al crear la oficina: en el alta pagada los siembra solo el paso que crea al dueño (sembrarlos
/// también aquí chocaba o se bloqueaba con él); en la oficina por invitación los siembra este consumidor.
/// </summary>
public sealed class TenantCreatedConsumerRoleSeedingIntegrationTests
{
    [Fact]
    public async Task Paid_signup_leaves_the_system_roles_to_the_owner_step()
    {
        var tenantId = Guid.NewGuid();

        await HandleAsync(tenantId, onboardingId: Guid.NewGuid());

        Assert.Equal(0, await CountSystemRolesAsync(tenantId));
    }

    [Fact]
    public async Task Invited_office_gets_its_system_roles_from_the_consumer()
    {
        var tenantId = Guid.NewGuid();

        await HandleAsync(tenantId, onboardingId: null);

        Assert.Equal(3, await CountSystemRolesAsync(tenantId));
    }

    private static async Task HandleAsync(Guid tenantId, Guid? onboardingId)
    {
        await using var db = NewContext();
        await TenantCreatedConsumer.Handle(
            new TenantCreatedIntegrationEvent
            {
                NewTenantId = tenantId,
                Name = "Seed Tax",
                SubDomain = $"seed{tenantId:N}"[..20],
                AdminEmail = "owner@seed.test",
                AdminInvitationTokenHash = new string('c', 64),
                AdminInvitationExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                OnboardingId = onboardingId,
            },
            new TenantRegistry(db),
            new InvitationRepository(db),
            new RoleRepository(db),
            new MfaRepository(db),
            new TenantDomainRepository(db),
            new TenantSubdomainReservationRepository(db),
            FixedPlatformSettings.For("taxoffix.io"),
            new FakeTokenReferenceStore(),
            new AccountTokenProtectionTests.CapturingAccountTokenProtector(),
            db,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<TenantCreatedIntegrationEvent>.Instance,
            CancellationToken.None
        );
    }

    private static async Task<int> CountSystemRolesAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await db.Roles.IgnoreQueryFilters().CountAsync(role => role.TenantId == tenantId && role.IsSystem);
    }

    private static AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(AuthIntegrationDatabase.ConnectionString).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
