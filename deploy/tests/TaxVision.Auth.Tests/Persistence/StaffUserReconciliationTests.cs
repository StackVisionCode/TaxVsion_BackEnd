using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// La lista de staff con la que Customer reconcilia su directorio de empleados: todos los tenants, solo
/// TenantAdmin y TenantEmployee, y con su estado real (activo, desactivado o retirado), porque el
/// directorio tiene que reflejar también a quien ya no está.
/// </summary>
public sealed class StaffUserReconciliationTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static AuthDbContext CreateContext(string databaseName)
    {
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(TenantA);
        return new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );
    }

    private static User Staff(Guid tenantId, string email, UserActorType actorType) =>
        User.Register(tenantId, "Staff", "User", email, "hash", actorType).Value;

    [Fact]
    public async Task Lists_staff_of_every_tenant_with_their_real_state()
    {
        var databaseName = Guid.NewGuid().ToString();
        var owner = Staff(TenantA, "owner@a.test", UserActorType.TenantAdmin);
        var deactivated = Staff(TenantA, "off@a.test", UserActorType.TenantEmployee);
        deactivated.Deactivate(DateTime.UtcNow);
        var offboarded = Staff(TenantB, "gone@b.test", UserActorType.TenantEmployee);
        Assert.True(offboarded.Offboard(DateTime.UtcNow).IsSuccess);
        await using (var seed = CreateContext(databaseName))
        {
            seed.Users.AddRange(
                owner,
                deactivated,
                offboarded,
                Staff(PlatformTenant.Id, "platform@x.test", UserActorType.PlatformAdmin),
                User.Register(
                    TenantB,
                    "Client",
                    "Portal",
                    "client@b.test",
                    "hash",
                    UserActorType.CustomerPortal,
                    Guid.NewGuid()
                ).Value
            );
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var page = await new UserRepository(db).ListStaffUsersForReconciliationAsync(1, 50);

        Assert.Equal(3, page.TotalCount);
        var byUser = page.Items.ToDictionary(item => item.UserId);
        Assert.Equal(new[] { owner.Id, deactivated.Id, offboarded.Id }.Order(), byUser.Keys.Order());
        Assert.Equal(("TenantAdmin", true, false), Shape(byUser[owner.Id]));
        Assert.Equal(("TenantEmployee", false, false), Shape(byUser[deactivated.Id]));
        Assert.Equal(("TenantEmployee", false, true), Shape(byUser[offboarded.Id]));
        Assert.Equal(TenantB, byUser[offboarded.Id].TenantId);
    }

    [Fact]
    public async Task Pages_through_the_staff_without_repeating_users()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(databaseName))
        {
            for (var i = 0; i < 5; i++)
                seed.Users.Add(Staff(i % 2 == 0 ? TenantA : TenantB, $"user{i}@x.test", UserActorType.TenantEmployee));
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new UserRepository(db);
        var first = await repository.ListStaffUsersForReconciliationAsync(1, 2);
        var second = await repository.ListStaffUsersForReconciliationAsync(2, 2);
        var third = await repository.ListStaffUsersForReconciliationAsync(3, 2);

        Assert.Equal(5, first.TotalCount);
        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.UserId).ToList();
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Single(third.Items);
    }

    private static (string ActorType, bool IsActive, bool IsOffboarded) Shape(
        TaxVision.Auth.Application.Users.StaffUserReconciliationResponse item
    ) => (item.ActorType, item.IsActive, item.IsOffboarded);
}
