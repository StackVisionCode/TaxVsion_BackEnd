using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

/// <summary>
/// GW-11 — al middleware le quedó una sola responsabilidad, y es la que importaba: borrar el
/// <c>X-Tenant-Id</c> que venga del caller para que nadie lo cuele hasta un upstream.
///
/// <para>
/// La mitad propagadora (reponerlo desde el claim del JWT) se eliminó: ningún servicio lo consume
/// —verificado en los 17 .NET y los 2 Node— porque todos derivan el tenant de su propio JWT vía
/// <c>JwtTenantContextMiddleware</c>. Estos tests fijan que el header <b>nunca</b> sale del Gateway,
/// ni siquiera con un JWT válido: si alguien reintroduce la propagación, fallan.
/// </para>
/// </summary>
public sealed class TenantPropagationMiddlewareTests
{
    private static async Task<HttpContext> InvokeAsync(string? incomingHeader = null, string? tenantClaim = null)
    {
        var context = new DefaultHttpContext();

        if (incomingHeader is not null)
            context.Request.Headers["X-Tenant-Id"] = incomingHeader;

        if (tenantClaim is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantClaim)], "TestAuth"));

        await new TenantPropagationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task Descarta_el_X_Tenant_Id_que_manda_el_caller()
    {
        var context = await InvokeAsync(incomingHeader: "11111111-1111-1111-1111-111111111111");

        Assert.False(context.Request.Headers.ContainsKey("X-Tenant-Id"));
    }

    [Fact]
    public async Task El_header_inyectado_no_sobrevive_ni_con_un_JWT_valido()
    {
        // El caso de spoofing real: header de un tenant, token de otro. Antes ganaba el claim y el
        // header se reponia; ahora simplemente no sale ninguno.
        var context = await InvokeAsync(
            incomingHeader: "11111111-1111-1111-1111-111111111111",
            tenantClaim: "22222222-2222-2222-2222-222222222222"
        );

        Assert.False(context.Request.Headers.ContainsKey("X-Tenant-Id"));
    }

    [Fact]
    public async Task No_propaga_el_tenant_del_claim_a_un_header()
    {
        // GW-11: propagar un valor que nadie lee deja preparado el dia en que alguien lo lea
        // "porque ya viene puesto" y convierta un header en autoridad de tenant.
        var context = await InvokeAsync(tenantClaim: "33333333-3333-3333-3333-333333333333");

        Assert.False(context.Request.Headers.ContainsKey("X-Tenant-Id"));
    }

    [Fact]
    public async Task Sin_header_ni_claim_no_inventa_nada()
    {
        var context = await InvokeAsync();

        Assert.False(context.Request.Headers.ContainsKey("X-Tenant-Id"));
    }

    [Fact]
    public async Task Deja_pasar_el_resto_de_headers()
    {
        // El middleware es quirurgico: solo toca X-Tenant-Id.
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "abc-123";
        context.Request.Headers["X-Tenant-Id"] = "44444444-4444-4444-4444-444444444444";

        await new TenantPropagationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-Tenant-Id"));
        Assert.Equal("abc-123", context.Request.Headers["X-Correlation-Id"]);
    }

    [Fact]
    public async Task Llama_al_siguiente_middleware()
    {
        var called = false;
        var context = new DefaultHttpContext();

        await new TenantPropagationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.True(called);
    }
}
