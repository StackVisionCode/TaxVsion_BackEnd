using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Estado de una sesión para el equipo: viva o vencida según la política, o cómo terminó según su motivo.
/// </summary>
public sealed class SessionStatusTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(15);

    [Theory]
    [InlineData("user_logout", SessionStatus.SignedOut)]
    [InlineData("user_logout_all", SessionStatus.SignedOut)]
    [InlineData("account_logout", SessionStatus.SignedOut)]
    [InlineData("admin_revoke", SessionStatus.Revoked)]
    [InlineData("single_session_superseded", SessionStatus.Replaced)]
    [InlineData("expired", SessionStatus.Expired)]
    [InlineData("idle_timeout", SessionStatus.Expired)]
    [InlineData("absolute_lifetime", SessionStatus.Expired)]
    [InlineData("password_change", SessionStatus.ClosedForSecurity)]
    [InlineData("tenant_suspended", SessionStatus.ClosedForSecurity)]
    public void A_closed_session_takes_its_status_from_the_reason(string reason, SessionStatus expected)
    {
        Assert.Equal(expected, SessionStatuses.Of(Now, reason, withinPolicy: true));
    }

    [Fact]
    public void An_open_session_inside_the_policy_is_active()
    {
        Assert.Equal(SessionStatus.Active, SessionStatuses.Of(null, null, withinPolicy: true));
    }

    [Fact]
    public void An_open_session_outside_the_policy_is_expired()
    {
        Assert.Equal(SessionStatus.Expired, SessionStatuses.Of(null, null, withinPolicy: false));
    }

    // La ventana y el corte en el refresh son la misma regla: no puede haber una sesión "activa" que el
    // siguiente refresh vaya a cortar, ni al revés.
    [Theory]
    [InlineData(0, true)]
    [InlineData(46, true)]
    [InlineData(47, false)]
    [InlineData(720, false)]
    public void The_activity_window_agrees_with_the_refresh_rule(int minutesAfterStart, bool expected)
    {
        var policy = PlatformSessionPolicy.CreateDefault(Now);
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), null, null, null);
        var now = session.CreatedAtUtc.AddMinutes(minutesAfterStart);

        var covered = policy
            .ActivityWindow(Refresh, now)
            .Covers(session.Channel, session.CreatedAtUtc, session.LastSeenAtUtc);

        Assert.Equal(policy.EndReason(session, Refresh, now) is null, covered);
        Assert.Equal(expected, covered);
    }

    [Fact]
    public void A_web_session_past_the_absolute_lifetime_is_outside_even_if_just_seen()
    {
        var window = PlatformSessionPolicy.CreateDefault(Now).ActivityWindow(Refresh, Now);

        Assert.False(window.Covers(SessionChannel.Web, Now.AddHours(-12), Now));
        Assert.True(window.Covers(SessionChannel.Web, Now.AddHours(-11), Now));
    }

    [Fact]
    public void A_mobile_session_is_always_inside_the_web_policy()
    {
        var window = PlatformSessionPolicy.CreateDefault(Now).ActivityWindow(Refresh, Now);

        Assert.True(window.Covers(SessionChannel.Mobile, Now.AddDays(-30), Now.AddDays(-30)));
    }
}
