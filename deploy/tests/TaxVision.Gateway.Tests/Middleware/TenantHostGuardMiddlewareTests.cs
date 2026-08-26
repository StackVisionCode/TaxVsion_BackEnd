using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

/// <summary>
/// TenantHostGuardMiddleware (Tarea 3): subdominio de oficina no registrado → 404 plano; tenant del
/// JWT ≠ tenant del Host → 403; hosts de sistema pasan sin consultar a Auth; fail-open si Auth no
/// responde. La resolución Host→tenant se mockea (el resolver real llama al by-host de Auth por red).
/// </summary>
public sealed class TenantHostGuardMiddlewareTests
{
    private static readonly Guid OfficeTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeResolver(HostTenantResult result) : IHostTenantResolver
    {
        public int Calls { get; private set; }

        public Task<HostTenantResult> ResolveAsync(string host, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static TenantHostGuardOptions DefaultOptions(bool enabled = true) =>
        new()
        {
            Enabled = enabled,
            BaseDomain = "taxproffice.com",
            SystemSubdomains = ["api", "app", "www", "admin"],
        };

    private static HostTenantResult Resolved(Guid tenantId) => new(HostTenantOutcome.Resolved, tenantId);

    private static async Task<(int StatusCode, bool ReachedNext, int ResolverCalls)> InvokeAsync(
        string host,
        HostTenantResult resolverResult,
        TenantHostGuardOptions? options = null,
        string? method = null,
        Guid? jwtTenantId = null
    )
    {
        var reachedNext = false;
        var resolver = new FakeResolver(resolverResult);
        var middleware = new TenantHostGuardMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            Options.Create(options ?? DefaultOptions()),
            NullLogger<TenantHostGuardMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        if (method is not null)
            context.Request.Method = method;
        if (jwtTenantId is { } tid)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tid.ToString())], "test"));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, resolver);
        return (context.Response.StatusCode, reachedNext, resolver.Calls);
    }

    [Theory]
    [InlineData("api.taxproffice.com")]
    [InlineData("app.taxproffice.com")]
    [InlineData("www.taxproffice.com")]
    [InlineData("admin.taxproffice.com")]
    [InlineData("taxproffice.com")] // apex
    [InlineData("localhost")]
    [InlineData("otro-dominio.com")]
    public async Task Deja_pasar_los_hosts_de_sistema_sin_consultar_a_Auth(string host)
    {
        var (_, reachedNext, calls) = await InvokeAsync(host, Resolved(OfficeTenant));

        Assert.True(reachedNext);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Subdominio_no_registrado_devuelve_404_y_no_pasa()
    {
        var (status, reachedNext, _) = await InvokeAsync(
            "inexistente.taxproffice.com",
            new HostTenantResult(HostTenantOutcome.NotRegistered, Guid.Empty)
        );

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(reachedNext);
    }

    [Fact]
    public async Task Oficina_resuelta_sin_JWT_pasa()
    {
        var (_, reachedNext, _) = await InvokeAsync("manfer.taxproffice.com", Resolved(OfficeTenant));

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task Oficina_resuelta_con_JWT_del_mismo_tenant_pasa()
    {
        var (_, reachedNext, _) = await InvokeAsync(
            "manfer.taxproffice.com",
            Resolved(OfficeTenant),
            jwtTenantId: OfficeTenant
        );

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task JWT_de_otro_tenant_en_el_host_de_una_oficina_devuelve_403_y_no_pasa()
    {
        var (status, reachedNext, _) = await InvokeAsync(
            "manfer.taxproffice.com",
            Resolved(OfficeTenant),
            jwtTenantId: OtherTenant
        );

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(reachedNext);
    }

    [Fact]
    public async Task Auth_no_disponible_hace_fail_open_aunque_el_JWT_no_cuadre()
    {
        var (_, reachedNext, _) = await InvokeAsync(
            "manfer.taxproffice.com",
            new HostTenantResult(HostTenantOutcome.Unavailable, Guid.Empty),
            jwtTenantId: OtherTenant
        );

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task Preflight_OPTIONS_pasa_sin_consultar_a_Auth()
    {
        var (_, reachedNext, calls) = await InvokeAsync(
            "inexistente.taxproffice.com",
            new HostTenantResult(HostTenantOutcome.NotRegistered, Guid.Empty),
            method: "OPTIONS"
        );

        Assert.True(reachedNext);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Deshabilitado_deja_pasar_todo()
    {
        var (_, reachedNext, calls) = await InvokeAsync(
            "inexistente.taxproffice.com",
            new HostTenantResult(HostTenantOutcome.NotRegistered, Guid.Empty),
            options: DefaultOptions(enabled: false)
        );

        Assert.True(reachedNext);
        Assert.Equal(0, calls);
    }
}
