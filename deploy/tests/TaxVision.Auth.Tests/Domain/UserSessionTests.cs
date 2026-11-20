using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// La sesión única se cuenta por canal: un cliente puede tener a la vez una sesión web (CRM, portal o Account)
/// y una de la app móvil, pero no dos del mismo canal.
/// </summary>
public sealed class UserSessionTests
{
    private static UserSession Start(SessionSurface surface) =>
        UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), "Device", "ua", "127.0.0.1", surface);

    [Fact]
    public void A_mobile_sign_in_starts_a_mobile_session()
    {
        Assert.Equal(SessionChannel.Mobile, Start(SessionSurface.Mobile).Channel);
    }

    [Theory]
    [InlineData(SessionSurface.Workspace)]
    [InlineData(SessionSurface.Account)]
    public void A_browser_sign_in_starts_a_web_session(SessionSurface surface)
    {
        Assert.Equal(SessionChannel.Web, Start(surface).Channel);
    }

    [Fact]
    public void Without_a_surface_the_session_is_web_as_before()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), "Device", "ua", "127.0.0.1");

        Assert.Equal(SessionChannel.Web, session.Channel);
    }

    [Fact]
    public void Web_and_mobile_sessions_do_not_compete_for_the_single_session()
    {
        var web = Start(SessionSurface.Workspace);
        var mobile = Start(SessionSurface.Mobile);

        Assert.False(web.CompetesWith(SessionSurface.Mobile));
        Assert.False(mobile.CompetesWith(SessionSurface.Workspace));
        Assert.True(web.CompetesWith(SessionSurface.Account));
        Assert.True(mobile.CompetesWith(SessionSurface.Mobile));
    }
}
