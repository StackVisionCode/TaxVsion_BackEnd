using BuildingBlocks.Messaging.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    /// Los defaults son el gate que rige si alguien despliega sin la sección <c>GatewayRateLimiting</c>.
    /// Este test rompe si se cambia un default sin querer.
    /// </summary>
    [Fact]
    public void Los_defaults_son_el_gate_pre_auth_vigente()
    {
        var options = new GatewayRateLimitOptions();

        Assert.Equal(30, options.PreAuthByIp.PermitLimit);
        Assert.Equal(60, options.PreAuthByIp.WindowSeconds);

        string[] expectedPreAuth =
        [
            "/auth/login",
            "/auth/mfa/verify",
            "/auth/password/forgot",
            "/auth/password/reset",
            "/auth/me/email/confirm",
            "/auth/invitations/accept",
            "/tenants",
        ];
        Assert.Equal(expectedPreAuth, options.PreAuthByIp.Rules.Select(r => r.Pattern));

        // Solo /tenants está condicionado por verbo (POST = alta de tenant).
        Assert.Equal("POST", Assert.Single(options.PreAuthByIp.Rules, r => r.Method is not null).Method);
    }

    /// <summary>
    /// Un 429 en el refresh deslogueaba al usuario, y la lista/creación de invitaciones del admin es
    /// tráfico autenticado con su propia política: ninguno de los dos pasa por el gate por IP.
    /// </summary>
    [Theory]
    [InlineData("/auth/refresh", "POST")]
    [InlineData("/auth/invitations", "GET")]
    [InlineData("/auth/invitations", "POST")]
    public void Refresh_e_invitaciones_del_admin_no_pasan_por_el_gate_pre_auth(string path, string method)
    {
        Assert.DoesNotContain(new GatewayRateLimitOptions().PreAuthByIp.Rules, rule => rule.Matches(path, method));
    }

    /// <summary>
    /// El upload ya lo acota cloudstorage.i.upload en el servicio; el grupo del Gateway queda sin reglas
    /// (disponible para reactivarlo por configuración en un incidente).
    /// </summary>
    [Fact]
    public void La_cuota_de_upload_del_gateway_queda_sin_reglas_por_defecto()
    {
        Assert.Empty(new GatewayRateLimitOptions().StorageUploadByTenant.Rules);
    }

    private static GatewayRateLimitOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddTaxVisionGatewayRateLimiting(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<GatewayRateLimitOptions>>().Value;
    }

    /// <summary>
    /// La configuración tiene que REEMPLAZAR los defaults, no sumarse: el binder agrega los elementos a
    /// los que ya trae la instancia, así que quitar una ruta de appsettings no la sacaba del gate.
    /// </summary>
    [Fact]
    public void Las_reglas_de_configuracion_reemplazan_a_las_de_codigo()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["GatewayRateLimiting:PreAuthByIp:PermitLimit"] = "15",
                ["GatewayRateLimiting:PreAuthByIp:WindowSeconds"] = "60",
                ["GatewayRateLimiting:PreAuthByIp:Rules:0:Pattern"] = "/auth/login",
            }
        );

        Assert.Equal(15, options.PreAuthByIp.PermitLimit);
        Assert.Equal(["/auth/login"], options.PreAuthByIp.Rules.Select(r => r.Pattern));
    }

    [Fact]
    public void Sin_reglas_en_configuracion_rigen_los_defaults()
    {
        var options = Resolve(
            new Dictionary<string, string?> { ["GatewayRateLimiting:PreAuthByIp:PermitLimit"] = "40" }
        );

        Assert.Equal(40, options.PreAuthByIp.PermitLimit);
        Assert.Equal(
            new GatewayRateLimitOptions().PreAuthByIp.Rules.Select(r => r.Pattern),
            options.PreAuthByIp.Rules.Select(r => r.Pattern)
        );
    }
}
