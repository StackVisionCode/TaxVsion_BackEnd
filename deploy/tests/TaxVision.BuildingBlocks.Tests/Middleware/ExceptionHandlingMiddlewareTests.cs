using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Middleware;

/// <summary>
/// BB-08 + H-18. El middleware es el último recurso de todos los servicios: lo que no atrapa sale
/// como un 500 sin forma. Se fija el mapeo de las 3 excepciones que traduce y, sobre todo, que no
/// intente reescribir una respuesta que ya empezó (H-18).
/// </summary>
public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<HttpContext> InvokeAsync(Exception toThrow)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return ctx;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext ctx) =>
        (await JsonDocument.ParseAsync(ctx.Response.Body)).RootElement;

    [Fact]
    public async Task ConflictException_SeTraduceA409ConSuCodigoDeDominio()
    {
        var ctx = await InvokeAsync(new ConflictException("Note.AlreadyArchived", "Already archived."));

        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
        var problem = await ReadProblemAsync(ctx);
        Assert.Equal("Note.AlreadyArchived", problem.GetProperty("code").GetString());
        Assert.Equal("Already archived.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_SeTraduceA401ParaQueElFrontRefresqueElToken()
    {
        // RBAC Fase 7: lo lanza ProjectionPermissionsSource cuando el perm_v del JWT quedó atrás.
        var ctx = await InvokeAsync(new UnauthorizedAccessException("Auth.StalePermissions"));

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.Equal("Auth.StalePermissions", (await ReadProblemAsync(ctx)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnaExcepcionCualquiera_SeTraduceA500SinFiltrarElMensajeInterno()
    {
        var ctx = await InvokeAsync(new InvalidOperationException("connection string: Server=prod;Password=hunter2"));

        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
        var problem = await ReadProblemAsync(ctx);
        Assert.Equal("Server.Unexpected", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("hunter2", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ConLaRespuestaYaEmpezada_RelanzaEnVezDeEnmascararLaExcepcionOriginal()
    {
        // H-18: escribir StatusCode con HasStarted=true lanza InvalidOperationException dentro del
        // catch, sustituyendo la excepción real por una sin relación. Debe salir la original.
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var original = new InvalidOperationException("el fallo de verdad");

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw original,
            NullLogger<ExceptionHandlingMiddleware>.Instance
        );

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(ctx));
        Assert.Same(original, thrown);
    }

    /// <summary>
    /// La feature por defecto de <see cref="DefaultHttpContext"/> reporta <c>HasStarted = false</c>
    /// siempre, escribas o no en el body — hace falta una propia para simular el caso de H-18.
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public bool HasStarted => true;

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
