using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// La lista con la que Communication repara su directorio de usuarios: todos los tenants y todos los actor
/// types, con nombre completo, email, estado real y foto, porque el chat muestra a staff y clientes por igual.
/// </summary>
public sealed class UserDirectoryReconciliationTests
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

    private static User Register(
        Guid tenantId,
        string name,
        string lastName,
        string email,
        UserActorType actorType,
        Guid? customerId = null
    ) => User.Register(tenantId, name, lastName, email, "hash", actorType, customerId).Value;

    [Fact]
    public async Task Lists_every_actor_type_of_every_tenant_with_name_email_state_and_avatar()
    {
        var databaseName = Guid.NewGuid().ToString();
        var admin = Register(TenantA, "Ana", "Owner", "ana@a.test", UserActorType.TenantAdmin);
        var avatarId = Guid.NewGuid();
        Assert.True(admin.BeginAvatarChange(avatarId, DateTime.UtcNow).IsSuccess);
        admin.ConfirmAvatar(avatarId);
        var deactivated = Register(TenantA, "Eli", "Off", "eli@a.test", UserActorType.TenantEmployee);
        deactivated.Deactivate(DateTime.UtcNow);
        var portal = Register(TenantB, "Cli", "Ent", "client@b.test", UserActorType.CustomerPortal, Guid.NewGuid());
        var platform = Register(PlatformTenant.Id, "Pat", "Support", "pat@x.test", UserActorType.PlatformAdmin);
        await using (var seed = CreateContext(databaseName))
        {
            seed.Users.AddRange(admin, deactivated, portal, platform);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var page = await new UserRepository(db).ListUsersForDirectoryReconciliationAsync(1, 50);

        Assert.Equal(4, page.TotalCount);
        var byUser = page.Items.ToDictionary(item => item.UserId);
        Assert.Equal(
            new UserDirectoryReconciliationResponse(
                TenantA,
                admin.Id,
                "TenantAdmin",
                true,
                "Ana Owner",
                "ana@a.test",
                avatarId
            ),
            byUser[admin.Id]
        );
        Assert.False(byUser[deactivated.Id].IsActive);
        Assert.Null(byUser[deactivated.Id].AvatarFileId);
        Assert.Equal(("CustomerPortal", TenantB, "Cli Ent"), Shape(byUser[portal.Id]));
        Assert.Equal(("PlatformAdmin", PlatformTenant.Id, "Pat Support"), Shape(byUser[platform.Id]));
    }

    [Fact]
    public async Task Pages_through_every_user_without_repeating_and_caps_the_page_size()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(databaseName))
        {
            for (var i = 0; i < 5; i++)
                seed.Users.Add(
                    Register(
                        i % 2 == 0 ? TenantA : TenantB,
                        "User",
                        $"N{i}",
                        $"u{i}@x.test",
                        UserActorType.TenantEmployee
                    )
                );
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new UserRepository(db);
        var first = await repository.ListUsersForDirectoryReconciliationAsync(1, 2);
        var second = await repository.ListUsersForDirectoryReconciliationAsync(2, 2);
        var third = await repository.ListUsersForDirectoryReconciliationAsync(3, 2);
        var huge = await repository.ListUsersForDirectoryReconciliationAsync(1, 10_000);

        Assert.Equal(5, first.TotalCount);
        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.UserId).ToList();
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Single(third.Items);
        Assert.Equal(500, huge.Size);
    }

    private static (string ActorType, Guid TenantId, string DisplayName) Shape(
        UserDirectoryReconciliationResponse item
    ) => (item.ActorType, item.TenantId, item.DisplayName);
}
