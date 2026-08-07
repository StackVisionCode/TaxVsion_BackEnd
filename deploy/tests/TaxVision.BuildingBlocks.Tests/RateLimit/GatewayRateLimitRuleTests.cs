using BuildingBlocks.Web.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// GW-12 — el gate pre-auth del Gateway pasó de una cadena de <c>path.Equals(...)</c> en C# a reglas
/// en configuración. Estos tests fijan la semántica del matcher: es lo único que ya no valida el
/// compilador.
/// </summary>
public sealed class GatewayRateLimitRuleTests
{
    [Theory]
    [InlineData("/auth/login", "POST")]
    [InlineData("/AUTH/LOGIN", "POST")]
    [InlineData("/auth/login/", "GET")]
    public void Sin_Method_declarado_matchea_cualquier_verbo(string path, string method)
    {
        var rule = new GatewayRateLimitRule { Pattern = "/auth/login" };

        Assert.True(rule.Matches(path, method));
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("GET", false)]
    public void Con_Method_declarado_solo_matchea_ese_verbo(string method, bool expected)
    {
        var rule = new GatewayRateLimitRule { Pattern = "/tenants", Method = "POST" };

        Assert.Equal(expected, rule.Matches("/tenants", method));
    }

    [Theory]
    [InlineData("/storage/files/11111111-1111-1111-1111-111111111111/complete", true)]
    [InlineData("/storage/files/anything/complete", true)]
    // El comodín cubre un segmento, no varios: estos no son el endpoint de complete.
    [InlineData("/storage/files/complete", false)]
    [InlineData("/storage/files/a/b/complete", false)]
    [InlineData("/storage/files/11111111/download-url", false)]
    public void El_comodin_cubre_exactamente_un_segmento(string path, bool expected)
    {
        var rule = new GatewayRateLimitRule { Pattern = "/storage/files/*/complete", Method = "POST" };

        Assert.Equal(expected, rule.Matches(path, "POST"));
    }

    [Theory]
    // Un prefijo no basta: /auth/invitations no debe capturar /auth/invitations/{id}/cancel.
    [InlineData("/auth/invitations/abc/cancel")]
    [InlineData("/auth/invitations/accept")]
    [InlineData("/auth")]
    public void El_matcheo_es_por_path_completo_no_por_prefijo(string path)
    {
        var rule = new GatewayRateLimitRule { Pattern = "/auth/invitations" };

        Assert.False(rule.Matches(path, "POST"));
    }

    /// <summary>
    /// Los defaults de la clase son el comportamiento que estaba hardcodeado: si alguien despliega
    /// sin la sección <c>GatewayRateLimiting</c>, el gate sigue siendo el mismo. Este test rompe si
    /// se cambia un default sin querer.
    /// </summary>
    [Fact]
    public void Los_defaults_reproducen_el_gate_historico()
    {
        var options = new GatewayRateLimitOptions();

        Assert.Equal(10, options.PreAuthByIp.PermitLimit);
        Assert.Equal(60, options.PreAuthByIp.WindowSeconds);
        Assert.Equal(30, options.StorageUploadByTenant.PermitLimit);
        Assert.Equal(60, options.StorageUploadByTenant.WindowSeconds);

        string[] expectedPreAuth =
        [
            "/auth/login",
            "/auth/refresh",
            "/auth/mfa/verify",
            "/auth/password/forgot",
            "/auth/password/reset",
            "/auth/me/email/confirm",
            "/auth/invitations/accept",
            "/auth/invitations",
            "/tenants",
        ];
        Assert.Equal(expectedPreAuth, options.PreAuthByIp.Rules.Select(r => r.Pattern));

        // Solo /tenants estaba condicionado por verbo en la versión hardcodeada.
        Assert.Equal("POST", Assert.Single(options.PreAuthByIp.Rules.Where(r => r.Method is not null)).Method);
    }
}
