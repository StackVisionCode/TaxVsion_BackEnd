using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// El filtro por cliente que habilita la pestaña "Portal access" del CRM: las lecturas paginadas de
/// invitaciones y usuarios aceptan un <c>customerId</c> opcional. Solo casa con registros de portal
/// (los únicos que llevan CustomerId); las cuentas de staff quedan fuera. Sin él, el CRM no podría
/// correlacionar el estado del portal con el cliente de su perfil.
/// </summary>
public sealed class PortalAccessByCustomerTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();

    private static AuthDbContext CreateContext(string databaseName)
    {
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Tenant);
        return new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );
    }

    private static Invitation PortalInvite(Guid customerId, string email) =>
        Invitation
            .Create(
                Tenant,
                email,
                UserActorType.CustomerPortal,
                customerId,
                invitedByUserId: null,
                tokenHash: new string(email[0], 64),
                expiresAtUtc: DateTime.UtcNow.AddDays(7)
            )
            .Value;

    private static User PortalUser(Guid customerId, string email) =>
        User.Register(Tenant, "Client", "Portal", email, "hash", UserActorType.CustomerPortal, customerId).Value;

    [Fact]
    public async Task Invitations_are_filtered_to_the_requested_customer()
    {
        var db = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(db))
        {
            await seed.Invitations.AddRangeAsync(
                PortalInvite(CustomerA, "a@client.test"),
                PortalInvite(CustomerB, "b@client.test"),
                Invitation
                    .Create(
                        Tenant,
                        "staff@office.test",
                        UserActorType.TenantEmployee,
                        customerId: null,
                        invitedByUserId: null,
                        tokenHash: new string('s', 64),
                        expiresAtUtc: DateTime.UtcNow.AddDays(7)
                    )
                    .Value
            );
            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(db);
        var repo = new InvitationRepository(read);

        var (mine, mineTotal) = await repo.GetPagedAsync(
            Tenant,
            status: null,
            page: 1,
            size: 20,
            customerId: CustomerA
        );
        var (all, allTotal) = await repo.GetPagedAsync(Tenant, status: null, page: 1, size: 20, customerId: null);

        Assert.Equal(1, mineTotal);
        Assert.Equal(CustomerA, Assert.Single(mine).CustomerId);
        Assert.Equal(3, allTotal); // sin filtro: los 3 (2 de portal + staff)
    }

    [Fact]
    public async Task Users_are_filtered_to_the_requested_customer()
    {
        var db = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(db))
        {
            await seed.Users.AddRangeAsync(
                PortalUser(CustomerA, "a@client.test"),
                PortalUser(CustomerB, "b@client.test"),
                User.Register(Tenant, "Jane", "Staff", "staff@office.test", "hash", UserActorType.TenantEmployee).Value
            );
            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(db);
        var repo = new UserRepository(read);

        var (mine, mineTotal) = await repo.GetPagedAsync(
            Tenant,
            page: 1,
            size: 20,
            search: null,
            isActive: null,
            customerId: CustomerA
        );
        var (all, allTotal) = await repo.GetPagedAsync(
            Tenant,
            page: 1,
            size: 20,
            search: null,
            isActive: null,
            customerId: null
        );

        Assert.Equal(1, mineTotal);
        Assert.Equal(CustomerA, Assert.Single(mine).CustomerId);
        Assert.Equal(3, allTotal);
    }
}
