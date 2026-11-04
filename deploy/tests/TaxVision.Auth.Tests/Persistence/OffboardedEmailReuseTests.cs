using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// D-8 en los repositorios reales: el email de un retirado no ocupa el lugar (se puede invitar de nuevo) y,
/// con dos filas del mismo email, las búsquedas por email (login, reset, discover) resuelven siempre la no
/// retirada. Además, el retiro encuentra solo las invitaciones de staff pendientes que envió el que se va.
/// </summary>
public sealed class OffboardedEmailReuseTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string Email = "back@example.com";

    [Fact]
    public async Task An_offboarded_email_is_free_for_a_new_staff_account()
    {
        var db = Guid.NewGuid().ToString();
        await SeedAsync(db, Offboarded(Employee()));

        await using var read = CreateContext(db);
        var repo = new UserRepository(read);

        Assert.False(await repo.EmailExistsAsync(Tenant, Email, UserAccountKind.Staff));
    }

    [Fact]
    public async Task A_suspended_account_still_holds_its_email()
    {
        var db = Guid.NewGuid().ToString();
        var suspended = Employee();
        suspended.Deactivate(DateTime.UtcNow);
        await SeedAsync(db, suspended);

        await using var read = CreateContext(db);

        Assert.True(await new UserRepository(read).EmailExistsAsync(Tenant, Email, UserAccountKind.Staff));
    }

    [Fact]
    public async Task Lookup_by_email_resolves_the_live_account_over_the_offboarded_one()
    {
        var db = Guid.NewGuid().ToString();
        var former = Offboarded(Employee());
        var current = Employee();
        await SeedAsync(db, former, current);

        await using var read = CreateContext(db);
        var found = await new UserRepository(read).GetByEmailAsync(Tenant, Email, UserAccountKind.Staff);

        Assert.Equal(current.Id, found?.Id);
    }

    [Fact]
    public async Task Lookup_by_email_still_finds_the_offboarded_account_when_it_is_the_only_one()
    {
        var db = Guid.NewGuid().ToString();
        var former = Offboarded(Employee());
        await SeedAsync(db, former);

        await using var read = CreateContext(db);
        var found = await new UserRepository(read).GetByEmailAsync(Tenant, Email, UserAccountKind.Staff);

        Assert.Equal(former.Id, found?.Id);
        Assert.False(found!.IsActive);
    }

    [Fact]
    public async Task Offboarding_lists_only_the_pending_staff_invitations_the_leaver_sent()
    {
        var db = Guid.NewGuid().ToString();
        var leaver = Guid.NewGuid();
        var staff = Invite("hire@example.com", UserActorType.TenantEmployee, null, leaver, 'a');
        var portal = Invite("client@example.com", UserActorType.CustomerPortal, Guid.NewGuid(), leaver, 'b');
        var other = Invite("other@example.com", UserActorType.TenantEmployee, null, Guid.NewGuid(), 'c');
        var accepted = Invite("done@example.com", UserActorType.TenantEmployee, null, leaver, 'd');
        accepted.Accept(Guid.NewGuid(), DateTime.UtcNow);
        await using (var seed = CreateContext(db))
        {
            await seed.Invitations.AddRangeAsync(staff, portal, other, accepted);
            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(db);
        var pending = await new InvitationRepository(read).ListPendingStaffSentByAsync(Tenant, leaver);

        Assert.Equal(staff.Id, Assert.Single(pending).Id);
    }

    private static User Employee() =>
        User.Register(Tenant, "Ana", "Gomez", Email, "hash", UserActorType.TenantEmployee).Value;

    private static User Offboarded(User user)
    {
        user.Offboard(DateTime.UtcNow);
        return user;
    }

    private static Invitation Invite(
        string email,
        UserActorType actorType,
        Guid? customerId,
        Guid invitedBy,
        char token
    ) =>
        Invitation
            .Create(Tenant, email, actorType, customerId, invitedBy, new string(token, 64), DateTime.UtcNow.AddDays(7))
            .Value;

    private static async Task SeedAsync(string databaseName, params User[] users)
    {
        await using var seed = CreateContext(databaseName);
        await seed.Users.AddRangeAsync(users);
        await seed.SaveChangesAsync();
    }

    private static AuthDbContext CreateContext(string databaseName)
    {
        var tenantContext = new FixedTenantContext();
        tenantContext.SetTenant(Tenant);
        return new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
