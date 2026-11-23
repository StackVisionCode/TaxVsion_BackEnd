using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra el SQL Server local: la migración deja la fila global con los valores por defecto, y dos admins que
/// guardan a la vez no se pisan (el segundo recibe un conflicto). Restaura los valores al terminar.
/// </summary>
public sealed class PlatformSessionPolicyIntegrationTests : IAsyncLifetime
{
    private (int Idle, int Warning, int Absolute) _original;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        var policy = await new SessionPolicyRepository(db).GetAsync();
        _original = (policy!.IdleTimeoutMinutes, policy.WarningSeconds, policy.AbsoluteLifetimeHours);
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        var policy = await new SessionPolicyRepository(db).GetAsync();
        policy!.Change(_original.Idle, _original.Warning, _original.Absolute, "restore", Guid.Empty, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_migration_leaves_the_global_row()
    {
        await using var db = NewContext();

        var policy = await new SessionPolicyRepository(db).GetAsync();

        Assert.NotNull(policy);
        Assert.Equal(PlatformSessionPolicy.GlobalId, policy.Id);
        Assert.InRange(
            policy.IdleTimeoutMinutes,
            PlatformSessionPolicy.MinIdleMinutes,
            PlatformSessionPolicy.MaxIdleMinutes
        );
    }

    [Fact]
    public async Task Two_admins_saving_at_once_do_not_overwrite_each_other()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await new SessionPolicyRepository(first).GetAsync();
        var b = await new SessionPolicyRepository(second).GetAsync();

        a!.Change(20, 60, 10, "primero", Guid.NewGuid(), DateTime.UtcNow);
        await first.SaveChangesAsync();
        b!.Change(25, 60, 10, "segundo", Guid.NewGuid(), DateTime.UtcNow);

        await Assert.ThrowsAnyAsync<Exception>(() => second.SaveChangesAsync());
        await using var check = NewContext();
        Assert.Equal(20, (await new SessionPolicyRepository(check).GetAsync())!.IdleTimeoutMinutes);
    }

    private static AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(AuthIntegrationDatabase.ConnectionString).Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
