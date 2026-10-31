using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>
/// El mantenimiento descarta las fotos pendientes que el antivirus nunca respondió (más de diez
/// minutos), de todas las oficinas, sin tocar la foto confirmada ni las pendientes recientes.
/// </summary>
public sealed class AuthMaintenanceServiceAvatarTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task DiscardStalePendingAvatarsAsync_descarta_solo_las_vencidas()
    {
        var databaseName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var stale = AvatarDoubles.Employee();
        var confirmed = AvatarDoubles.GiveConfirmedAvatar(stale);
        stale.BeginAvatarChange(Guid.NewGuid(), now.AddMinutes(-30));
        var fresh = AvatarDoubles.Employee();
        var freshPending = Guid.NewGuid();
        fresh.BeginAvatarChange(freshPending, now.AddMinutes(-2));

        await using (var seed = CreateContext(databaseName))
        {
            await seed.Users.AddRangeAsync(stale, fresh);
            await seed.SaveChangesAsync();
        }

        int discarded;
        await using (var db = CreateContext(databaseName))
        {
            discarded = await AuthMaintenanceService.DiscardStalePendingAvatarsAsync(db, now, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext(databaseName);
        var reloadedStale = await check.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == stale.Id);
        var reloadedFresh = await check.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == fresh.Id);
        Assert.Equal(1, discarded);
        Assert.Null(reloadedStale.PendingAvatarFileId);
        Assert.Null(reloadedStale.PendingAvatarRequestedAtUtc);
        Assert.Equal(confirmed, reloadedStale.AvatarFileId);
        Assert.Equal(freshPending, reloadedFresh.PendingAvatarFileId);
    }

    // T-20: una foto en revisión de contenido espera al revisor, no es un antivirus que no respondió.
    [Fact]
    public async Task DiscardStalePendingAvatarsAsync_no_toca_las_que_estan_en_revision()
    {
        var databaseName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var reviewed = AvatarDoubles.Employee();
        var pending = Guid.NewGuid();
        reviewed.BeginAvatarChange(pending, now.AddHours(-3));
        Assert.True(reviewed.HoldPendingAvatarForReview(pending));

        await using (var seed = CreateContext(databaseName))
        {
            await seed.Users.AddAsync(reviewed);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var discarded = await AuthMaintenanceService.DiscardStalePendingAvatarsAsync(db, now, CancellationToken.None);

        Assert.Equal(0, discarded);
    }

    private static AuthDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            new NoTenantContext()
        );
}
