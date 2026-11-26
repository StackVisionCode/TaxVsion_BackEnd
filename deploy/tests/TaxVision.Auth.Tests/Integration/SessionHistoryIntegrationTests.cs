using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Historial de sesiones de una oficina contra SQL Server: solo personal, jerarquía, ventana, filtros,
/// conteos y la purga por retención. Cada prueba siembra su propia oficina.
/// </summary>
public sealed class SessionHistoryIntegrationTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static SessionActivityWindow Window =>
        PlatformSessionPolicy.CreateDefault(Now).ActivityWindow(TimeSpan.FromMinutes(15), Now);

    [Fact]
    public async Task The_list_shows_only_staff_of_the_office_newest_first_and_pages()
    {
        var office = await SeedOfficeAsync();

        await using var db = NewContext();
        var page = await new SessionRepository(db).ListAsync(
            new SessionHistoryFilter(
                new SessionHistoryScope(office.TenantId, true),
                Now.AddDays(-7),
                Now,
                Window,
                PageSize: 2
            )
        );

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Items[0].CreatedAtUtc >= page.Items[1].CreatedAtUtc);
        Assert.DoesNotContain(page.Items, row => row.UserId == office.Portal.Id);
    }

    [Fact]
    public async Task An_employee_scope_hides_admin_sessions()
    {
        var office = await SeedOfficeAsync();

        await using var db = NewContext();
        var page = await new SessionRepository(db).ListAsync(
            new SessionHistoryFilter(new SessionHistoryScope(office.TenantId, false), Now.AddDays(-7), Now, Window)
        );

        Assert.All(page.Items, row => Assert.Equal(UserActorType.TenantEmployee, row.UserActorType));
    }

    [Fact]
    public async Task Sessions_outside_the_period_are_left_out()
    {
        var office = await SeedOfficeAsync();
        var old = await AddSessionAsync(office.Employee, Now.AddDays(-100));

        await using var db = NewContext();
        var page = await new SessionRepository(db).ListAsync(
            new SessionHistoryFilter(new SessionHistoryScope(office.TenantId, true), Now.AddDays(-90), Now, Window)
        );

        Assert.DoesNotContain(page.Items, row => row.Id == old.Id);
    }

    [Fact]
    public async Task Status_and_search_filter_in_the_database()
    {
        var office = await SeedOfficeAsync();
        var scope = new SessionHistoryScope(office.TenantId, true);

        await using var db = NewContext();
        var repository = new SessionRepository(db);
        var revoked = await repository.ListAsync(
            new SessionHistoryFilter(scope, Now.AddDays(-7), Now, Window, Status: SessionStatus.Revoked)
        );
        var active = await repository.ListAsync(
            new SessionHistoryFilter(scope, Now.AddDays(-7), Now, Window, Status: SessionStatus.Active)
        );
        var byName = await repository.ListAsync(
            new SessionHistoryFilter(scope, Now.AddDays(-7), Now, Window, Search: "ernand")
        );

        Assert.Equal(office.RevokedSessionId, Assert.Single(revoked.Items).Id);
        Assert.Equal(2, active.TotalCount);
        Assert.All(byName.Items, row => Assert.Equal(office.Employee.Id, row.UserId));
        Assert.Equal(2, byName.TotalCount);
    }

    [Fact]
    public async Task The_counts_cover_the_office_staff()
    {
        var office = await SeedOfficeAsync();

        await using var db = NewContext();
        var counts = await new SessionRepository(db).CountAsync(
            new SessionHistoryScope(office.TenantId, true),
            Now.AddDays(-7),
            Now,
            Window
        );

        Assert.Equal(2, counts.ActiveNow);
        Assert.Equal(2, counts.MembersWithActiveSession);
        Assert.Equal(2, counts.StaffMembers);
        Assert.Equal(1, counts.SignedOutByAdmin);
        Assert.Equal(3, counts.SignInsUtc.Count);
    }

    [Fact]
    public async Task The_purge_removes_only_closed_sessions_older_than_the_cutoff()
    {
        var office = await SeedOfficeAsync();
        var oldClosed = await AddSessionAsync(office.Employee, Now.AddMonths(-14), revokedAt: Now.AddMonths(-13));
        var oldOpen = await AddSessionAsync(office.Employee, Now.AddMonths(-14));

        await using (var db = NewContext())
            await new SessionRepository(db).PurgeClosedBeforeAsync(Now.AddMonths(-12), 500);

        await using var check = NewContext();
        var left = await check
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.TenantId == office.TenantId)
            .Select(session => session.Id)
            .ToListAsync();
        Assert.DoesNotContain(oldClosed.Id, left);
        Assert.Contains(oldOpen.Id, left);
        Assert.Contains(office.RevokedSessionId, left);
    }

    [Fact]
    public async Task The_history_of_one_user_includes_a_portal_account()
    {
        var office = await SeedOfficeAsync();

        await using var db = NewContext();
        var page = await new SessionRepository(db).ListForUserAsync(
            office.TenantId,
            office.Portal.Id,
            Now.AddDays(-7),
            Now,
            1,
            20
        );

        var row = Assert.Single(page.Items);
        Assert.Equal(office.Portal.Id, row.UserId);
        Assert.Equal(UserActorType.CustomerPortal, row.UserActorType);
    }

    [Fact]
    public async Task The_history_of_one_user_stays_inside_the_office()
    {
        var office = await SeedOfficeAsync();

        await using var db = NewContext();
        var page = await new SessionRepository(db).ListForUserAsync(
            Guid.NewGuid(),
            office.Portal.Id,
            Now.AddDays(-7),
            Now,
            1,
            20
        );

        Assert.Empty(page.Items);
    }

    // ---------------------------------------------------------------- siembra

    private sealed record Office(Guid TenantId, User Admin, User Employee, User Portal, Guid RevokedSessionId);

    /// <summary>Un admin y un empleado con sesión activa, una sesión del empleado cerrada por un admin y un cliente del portal.</summary>
    private static async Task<Office> SeedOfficeAsync()
    {
        var tenantId = Guid.NewGuid();
        var admin = Register(tenantId, "Carla", "Castillo", UserActorType.TenantAdmin);
        var employee = Register(tenantId, "Andres", "Hernandez", UserActorType.TenantEmployee);
        var portal = Register(tenantId, "Minato", "Client", UserActorType.CustomerPortal, Guid.NewGuid());
        var foreign = Register(Guid.NewGuid(), "Otro", "Hernandez", UserActorType.TenantEmployee);

        await using (var db = NewContext())
        {
            db.Users.AddRange(admin, employee, portal, foreign);
            await db.SaveChangesAsync();
        }

        await AddSessionAsync(admin, Now.AddMinutes(-20));
        await AddSessionAsync(employee, Now.AddMinutes(-30));
        var revoked = await AddSessionAsync(employee, Now.AddDays(-2), revokedAt: Now.AddDays(-2).AddHours(1));
        await AddSessionAsync(portal, Now.AddMinutes(-10));
        await AddSessionAsync(foreign, Now.AddMinutes(-10));
        return new Office(tenantId, admin, employee, portal, revoked.Id);
    }

    private static User Register(
        Guid tenantId,
        string name,
        string lastName,
        UserActorType type,
        Guid? customerId = null
    ) => User.Register(tenantId, name, lastName, $"{Guid.NewGuid():N}@example.com", "hashed:x", type, customerId).Value;

    private static async Task<UserSession> AddSessionAsync(User user, DateTime startedAt, DateTime? revokedAt = null)
    {
        var session = UserSession.Start(
            user.TenantId,
            user.Id,
            "Chrome",
            "agent",
            "203.0.113.10",
            SessionSurface.Workspace
        );
        if (revokedAt is not null)
            session.Revoke("admin_revoke", Guid.NewGuid());

        await using var db = NewContext();
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        // La sesión nace "ahora"; la prueba la ubica en el tiempo que necesita.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserSessions SET CreatedAtUtc = {startedAt}, LastSeenAtUtc = {startedAt}, RevokedAtUtc = CASE WHEN RevokedAtUtc IS NULL THEN NULL ELSE {revokedAt} END WHERE Id = {session.Id}"
        );
        return session;
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
