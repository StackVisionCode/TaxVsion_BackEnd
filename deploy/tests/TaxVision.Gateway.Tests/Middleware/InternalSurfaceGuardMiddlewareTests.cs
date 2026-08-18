using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

public sealed class InternalSurfaceGuardMiddlewareTests
{
    private static async Task<(int StatusCode, bool ReachedNext)> InvokeAsync(string path)
    {
        var reachedNext = false;
        var middleware = new InternalSurfaceGuardMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<InternalSurfaceGuardMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, reachedNext);
    }

    /// <summary>
    /// Las 18 rutas M2M reales del sistema tras unificar la convención en <c>internal/*</c> (GW-02).
    /// Si aparece un controller interno nuevo, esta lista se queda corta pero el guard lo cubre igual
    /// — la lista existe para fijar las formas que ya sabemos que se dan.
    /// </summary>
    [Theory]
    [InlineData("/internal/invitations/token-references")]
    [InlineData("/internal/onboarding/tokens/abc/raw")]
    [InlineData("/internal/tenants/11111111-1111-1111-1111-111111111111/owners")]
    [InlineData(
        "/internal/tenants/11111111-1111-1111-1111-111111111111/users/22222222-2222-2222-2222-222222222222/permissions-snapshot"
    )]
    [InlineData("/internal/customers/list")]
    [InlineData("/internal/customers/reconciliation")]
    [InlineData("/internal/document-branding")]
    [InlineData("/internal/document-generations")]
    [InlineData("/internal/document-generations/onboarding-receipts")]
    [InlineData("/internal/codes/reservations")]
    [InlineData("/internal/referrals/qualifications")]
    [InlineData("/internal/onboarding/checkout")]
    [InlineData("/internal/payables")]
    [InlineData("/internal/plans/33333333-3333-3333-3333-333333333333/pricing")]
    [InlineData("/internal/plan-rate-limits")]
    [InlineData("/internal/subscriptions/activate-from-onboarding")]
    [InlineData("/internal/users")]
    [InlineData("/internal/tenants/subdomain-available")]
    [InlineData("/internal/tenants/from-onboarding")]
    public async Task Bloquea_con_404_las_rutas_internas_reales(string path)
    {
        var (statusCode, reachedNext) = await InvokeAsync(path);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.False(reachedNext);
    }

    /// <summary>
    /// GW-02 movió estos paths a <c>internal/*</c>, pero el guard no depende de esa migración: si
    /// alguien reintroduce un controller con el prefijo de servicio delante, sigue bloqueado.
    /// </summary>
    [Theory]
    [InlineData("/auth/internal/tenants/x/owners")]
    [InlineData("/customers/internal/reconciliation")]
    [InlineData("/payments-app/internal/onboarding/checkout")]
    [InlineData("/subscriptions/internal/plan-rate-limits")]
    [InlineData("/tenants/internal/from-onboarding")]
    public async Task Bloquea_tambien_el_segmento_internal_en_medio_del_path(string path)
    {
        var (statusCode, reachedNext) = await InvokeAsync(path);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.False(reachedNext);
    }

    [Theory]
    [InlineData("/internal")]
    [InlineData("/customers/internal")]
    [InlineData("/tenants/internal")]
    public async Task Bloquea_cuando_internal_es_el_ultimo_segmento(string path)
    {
        // Un `Contains("/internal/")` dejaría pasar los tres: no hay barra final.
        var (statusCode, reachedNext) = await InvokeAsync(path);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.False(reachedNext);
    }

    [Theory]
    [InlineData("/INTERNAL/codes")]
    [InlineData("/Internal/Tenants/subdomain-available")]
    public async Task El_bloqueo_es_insensible_a_mayusculas(string path)
    {
        var (statusCode, reachedNext) = await InvokeAsync(path);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.False(reachedNext);
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/customers")]
    [InlineData("/tenants/subdomain-available")]
    [InlineData("/health/ready")]
    [InlineData("/communication/socket.io/")]
    [InlineData("/storage/files")]
    // Empiezan por las mismas letras pero no son el segmento: deben pasar.
    [InlineData("/documents/internal-audit")]
    [InlineData("/reports/internals")]
    public async Task Deja_pasar_las_rutas_publicas(string path)
    {
        var (statusCode, reachedNext) = await InvokeAsync(path);

        Assert.True(reachedNext);
        Assert.Equal(StatusCodes.Status200OK, statusCode);
    }
}
