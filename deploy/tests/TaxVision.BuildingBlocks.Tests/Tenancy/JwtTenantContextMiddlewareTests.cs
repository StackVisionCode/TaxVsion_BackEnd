using System.Security.Claims;
using BuildingBlocks.Web.Tenancy;
using Microsoft.AspNetCore.Http;
using Wolverine;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Tenancy;

/// <summary>
/// BB-08. Este middleware decide el tenant de **toda** request autenticada: lo que ponga en
/// <see cref="TenantContext"/> es lo que filtra el <c>HasQueryFilter</c> global de cada DbContext, y
/// lo que estampa en <see cref="IMessageBus.TenantId"/> es lo único que hace que un handler de
/// Wolverine (que corre en otro DI scope) vea algo distinto de 0 filas. Los dos caminos que importan
/// son el rechazo del claim malformado y que ambas piezas se llenen a la vez.
/// </summary>
public sealed class JwtTenantContextMiddlewareTests
{
    private static async Task<(
        TenantContext Tenant,
        StubMessageBus Bus,
        HttpContext Ctx,
        bool ReachedNext
    )> InvokeAsync(ClaimsPrincipal user)
    {
        var ctx = new DefaultHttpContext { User = user };
        var tenant = new TenantContext();
        var bus = new StubMessageBus();
        var reachedNext = false;

        var middleware = new JwtTenantContextMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, tenant, bus);
        return (tenant, bus, ctx, reachedNext);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    [Fact]
    public async Task ConUnTenantIdValido_LlenaElContextoYElBus()
    {
        var tenantId = Guid.NewGuid();

        var (tenant, bus, _, reachedNext) = await InvokeAsync(
            Authenticated(new Claim("tenant_id", tenantId.ToString()))
        );

        Assert.True(tenant.HasTenant);
        Assert.Equal(tenantId, tenant.TenantId);
        // Sin esto, cualquier entidad ITenantOwned consultada dentro de un handler de Wolverine
        // devolvería 0 filas bajo la política fail-closed.
        Assert.Equal(tenantId.ToString(), bus.TenantId);
        Assert.True(reachedNext);
    }

    [Theory]
    [InlineData("no-es-un-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000")]
    public async Task ConUnTenantIdMalformado_Devuelve401YCortaElPipeline(string malformed)
    {
        var (tenant, bus, ctx, reachedNext) = await InvokeAsync(Authenticated(new Claim("tenant_id", malformed)));

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.False(reachedNext);
        Assert.False(tenant.HasTenant);
        Assert.Null(bus.TenantId);
    }

    [Fact]
    public async Task SinAutenticar_NoLlenaNadaYDejaPasar()
    {
        // Endpoints anónimos legítimos (login, JWKS, share links públicos). El filtro fail-closed
        // de cada DbContext se encarga: sin tenant, 0 filas para entidades tenant-owned.
        var (tenant, bus, ctx, reachedNext) = await InvokeAsync(Anonymous());

        Assert.False(tenant.HasTenant);
        Assert.Null(bus.TenantId);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.True(reachedNext);
    }

    [Fact]
    public async Task AutenticadoSinClaimDeTenant_DejaPasarSinTenant()
    {
        // Es el caso M2M sin tenant y el del PlatformAdmin cross-tenant: no es un error.
        var (tenant, bus, ctx, reachedNext) = await InvokeAsync(
            Authenticated(new Claim("sub", Guid.NewGuid().ToString()))
        );

        Assert.False(tenant.HasTenant);
        Assert.Null(bus.TenantId);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.True(reachedNext);
    }

    [Fact]
    public async Task ConUnTenantIdVacio_LoAceptaComoGuidValido()
    {
        // Guid.Empty parsea, así que el middleware lo deja pasar — y el filtro fail-closed de los
        // DbContext ya usa Guid.Empty como "sin tenant", o sea 0 filas. Documenta el borde.
        var (tenant, _, ctx, reachedNext) = await InvokeAsync(
            Authenticated(new Claim("tenant_id", Guid.Empty.ToString()))
        );

        Assert.True(tenant.HasTenant);
        Assert.Equal(Guid.Empty, tenant.TenantId);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.True(reachedNext);
    }

    /// <summary>Solo interesa <see cref="IMessageBus.TenantId"/>; el resto no se ejerce acá.</summary>
    private sealed class StubMessageBus : IMessageBus
    {
        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }
}
