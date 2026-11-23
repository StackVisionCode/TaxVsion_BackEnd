using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Domain;

public sealed class PlatformSessionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(15);

    [Fact]
    public void Default_is_thirty_minutes_sixty_seconds_and_twelve_hours()
    {
        var policy = PlatformSessionPolicy.CreateDefault(Now);

        Assert.Equal(PlatformSessionPolicy.GlobalId, policy.Id);
        Assert.Equal(30, policy.IdleTimeoutMinutes);
        Assert.Equal(60, policy.WarningSeconds);
        Assert.Equal(12, policy.AbsoluteLifetimeHours);
    }

    [Fact]
    public void Change_keeps_the_new_values_and_who_changed_them()
    {
        var policy = PlatformSessionPolicy.CreateDefault(Now);

        var changed = policy.Change(45, 90, 8, "Política de la oficina", Admin, Now.AddHours(1));

        Assert.True(changed.IsSuccess);
        Assert.Equal(45, policy.IdleTimeoutMinutes);
        Assert.Equal(90, policy.WarningSeconds);
        Assert.Equal(8, policy.AbsoluteLifetimeHours);
        Assert.Equal(Admin, policy.UpdatedBy);
        Assert.Equal(Now.AddHours(1), policy.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(4, 60, 12)]
    [InlineData(241, 60, 12)]
    [InlineData(30, 29, 12)]
    [InlineData(30, 301, 12)]
    [InlineData(30, 60, 0)]
    [InlineData(30, 60, 73)]
    public void Change_rejects_values_out_of_range(int idle, int warning, int absolute)
    {
        var policy = PlatformSessionPolicy.CreateDefault(Now);

        var changed = policy.Change(idle, warning, absolute, "r", Admin, Now);

        Assert.Equal("SessionPolicy.OutOfRange", changed.Error.Code);
        Assert.Equal(30, policy.IdleTimeoutMinutes);
    }

    // El aviso tiene que caber dentro de la inactividad: 5 min de inactividad con 5 min de aviso no tiene sentido.
    [Fact]
    public void Change_rejects_a_warning_as_long_as_the_idle_time()
    {
        var changed = PlatformSessionPolicy.CreateDefault(Now).Change(5, 300, 12, "r", Admin, Now);

        Assert.Equal("SessionPolicy.WarningTooLong", changed.Error.Code);
    }

    // La sesión no puede durar menos que la inactividad: se cortaría antes de avisar.
    [Fact]
    public void Change_rejects_an_absolute_lifetime_shorter_than_the_idle_time()
    {
        var changed = PlatformSessionPolicy.CreateDefault(Now).Change(120, 60, 1, "r", Admin, Now);

        Assert.Equal("SessionPolicy.AbsoluteShorterThanIdle", changed.Error.Code);
    }

    [Fact]
    public void Change_requires_a_reason()
    {
        var changed = PlatformSessionPolicy.CreateDefault(Now).Change(30, 60, 12, "  ", Admin, Now);

        Assert.Equal("SessionPolicy.ReasonRequired", changed.Error.Code);
    }

    [Fact]
    public void An_active_web_session_goes_on()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), null, null, null);

        Assert.Null(
            PlatformSessionPolicy.CreateDefault(Now).EndReason(session, Refresh, session.CreatedAtUtc.AddMinutes(10))
        );
    }

    // Sin actividad el front no renueva: pasada la inactividad + el aviso + una vida de token, la sesión se corta.
    [Fact]
    public void A_web_session_without_refreshes_for_too_long_ends_for_inactivity()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), null, null, null);
        var policy = PlatformSessionPolicy.CreateDefault(Now);
        var limit = TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(60) + Refresh;

        Assert.Null(policy.EndReason(session, Refresh, session.LastSeenAtUtc + limit));
        Assert.Equal(
            "idle_timeout",
            policy.EndReason(session, Refresh, session.LastSeenAtUtc + limit + TimeSpan.FromSeconds(1))
        );
    }

    [Fact]
    public void A_web_session_older_than_the_absolute_lifetime_ends_even_if_active()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), null, null, null);
        var policy = PlatformSessionPolicy.CreateDefault(Now);
        var atLimit = session.CreatedAtUtc.AddHours(12);
        session.Touch(null);

        Assert.Equal("absolute_lifetime", policy.EndReason(session, Refresh, atLimit));
    }

    // La app móvil tiene su propio ciclo de vida: esta política es de la web (CRM y portal).
    [Fact]
    public void Mobile_sessions_are_not_governed_by_this_policy()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), null, null, null, SessionSurface.Mobile);

        Assert.Null(
            PlatformSessionPolicy.CreateDefault(Now).EndReason(session, Refresh, session.CreatedAtUtc.AddDays(5))
        );
    }
}
