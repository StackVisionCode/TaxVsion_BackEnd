using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Middleware;

/// <summary>
/// BB-08. El correlation id entra por header, o sea que lo controla el cliente, y termina en los
/// logs de todos los servicios y en el header de respuesta. La validación por regex es la que impide
/// que alguien inyecte saltos de línea (log forging / header splitting) o un id de 10 KB.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    private static async Task<(HttpContext Ctx, string Observed)> InvokeAsync(string? incoming)
    {
        var ctx = new DefaultHttpContext();
        if (incoming is not null)
            ctx.Request.Headers[CorrelationIdMiddleware.Header] = incoming;

        var corr = new CorrelationContext();
        var observed = string.Empty;

        var middleware = new CorrelationIdMiddleware(_ =>
        {
            // Dentro del pipeline el id ya tiene que estar disponible: es lo que consumen los logs.
            observed = corr.CorrelationId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, corr);
        return (ctx, observed);
    }

    [Fact]
    public async Task SinHeader_GeneraUnIdNuevo()
    {
        var (ctx, observed) = await InvokeAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(observed));
        Assert.Equal(observed, ctx.Request.Headers[CorrelationIdMiddleware.Header]);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("req-2026.08.07_001")]
    [InlineData("A")]
    public async Task ConUnHeaderValido_LoRespeta(string incoming)
    {
        var (ctx, observed) = await InvokeAsync(incoming);

        Assert.Equal(incoming, observed);
        Assert.Equal(incoming, ctx.Request.Headers[CorrelationIdMiddleware.Header]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacios")]
    [InlineData("inyección\ncon salto")] // log forging
    [InlineData("punto;coma")]
    [InlineData("acentuado-ñ")]
    public async Task ConUnHeaderInvalido_LoDescartaYGeneraUnoLimpio(string incoming)
    {
        var (_, observed) = await InvokeAsync(incoming);

        Assert.NotEqual(incoming, observed);
        Assert.Matches("^[A-Za-z0-9._-]{1,128}$", observed);
    }

    [Fact]
    public async Task ConUnHeaderDemasiadoLargo_LoDescarta()
    {
        // 129 caracteres: uno por encima del máximo. Sin el cap, el id viaja a todos los logs.
        var (_, observed) = await InvokeAsync(new string('a', 129));

        Assert.Equal(32, observed.Length); // Guid "N"
    }

    [Fact]
    public async Task ConExactamente128Caracteres_LoAcepta()
    {
        var boundary = new string('a', 128);

        var (_, observed) = await InvokeAsync(boundary);

        Assert.Equal(boundary, observed);
    }
}
