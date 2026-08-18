using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Api.Middleware;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// Fase A3/A6 — aceptación: host conocido resuelve; host desconocido -> 404 + queda
/// auditado (TenantResolutionFailed); un X-Forwarded-Host falsificado se ignora
/// porque el middleware solo lee HttpContext.Request.Host (la confianza en ese
/// header, si el deploy la habilita, se resuelve antes por ForwardedHeadersMiddleware,
/// fuera de esta clase).
/// </summary>
public sealed class TenantHostResolutionMiddlewareTests
{
    private sealed class FakeTenantResolver : ITenantResolver
    {
        public string? LastRequestedHost { get; private set; }
        public Func<string?, HostResolutionResult> ResolveFn { get; set; } =
            _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        public Task<HostResolutionResult> ResolveAsync(string? host, CancellationToken ct = default)
        {
            LastRequestedHost = host;
            return Task.FromResult(ResolveFn(host));
        }
    }

    /// <summary>Cuenta por clave en memoria, con la misma semántica que RedisRateCounter: el
    /// primer incremento de una clave devuelve 1.</summary>
    private sealed class FakeRateCounter : IRateCounter
    {
        private readonly Dictionary<string, long> _counts = [];

        public Exception? ThrowOnIncrement { get; set; }

        public Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default)
        {
            if (ThrowOnIncrement is not null)
            {
                return Task.FromException<long>(ThrowOnIncrement);
            }

            _counts[key.Value] = _counts.GetValueOrDefault(key.Value) + 1;
            return Task.FromResult(_counts[key.Value]);
        }
    }

    private static (
        TenantHostResolutionMiddleware middleware,
        FakeTenantResolver resolver,
        ResolvedTenantContext tenantContext,
        FakeAuthAuditWriter audit,
        FakeUnitOfWork unitOfWork,
        FakeMessageBus bus,
        FakeRateCounter rateCounter,
        bool[] nextCalled
    ) BuildMiddleware(bool enforce)
    {
        var resolver = new FakeTenantResolver();
        var tenantContext = new ResolvedTenantContext();
        var audit = new FakeAuthAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var rateCounter = new FakeRateCounter();
        var nextCalled = new bool[1];
        var options = Options.Create(new TenantDomainOptions { EnforceHostResolution = enforce });
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };

        return (
            new TenantHostResolutionMiddleware(next, options, NullLogger<TenantHostResolutionMiddleware>.Instance),
            resolver,
            tenantContext,
            audit,
            unitOfWork,
            bus,
            rateCounter,
            nextCalled
        );
    }

    private static Task InvokeAsync(
        TenantHostResolutionMiddleware middleware,
        HttpContext context,
        ITenantResolver resolver,
        IResolvedTenantContext tenantContext,
        IAuthAuditWriter audit,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork,
        FakeMessageBus bus,
        IRateCounter rateCounter
    ) =>
        middleware.InvokeAsync(
            context,
            resolver,
            tenantContext,
            audit,
            unitOfWork,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            bus,
            rateCounter
        );

    [Fact]
    public async Task Known_host_resolves_and_calls_next()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: true
        );
        resolver.ResolveFn = _ => HostResolutionResult.Resolved(tenantId);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("oficina1.taxproffice.com");

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.True(nextCalled[0]);
        Assert.Equal(tenantId, tenantContext.ResolvedTenantId);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task Unknown_host_returns_404_never_calls_next_and_is_audited_when_enforced()
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: true
        );
        resolver.ResolveFn = _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("no-existe.taxproffice.com");

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Null(tenantContext.ResolvedTenantId);
        Assert.Single(audit.Logs, log => log.Action == AuthAuditAction.TenantResolutionFailed && !log.Success);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Single(
            bus.Published.OfType<TenantResolutionFailedIntegrationEvent>(),
            evt => evt.Host == "no-existe.taxproffice.com" && evt.Reason == "HostUnknown"
        );
    }

    [Fact]
    public async Task Unknown_host_falls_through_when_enforcement_disabled_but_is_still_audited()
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: false
        );
        resolver.ResolveFn = _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost:5124");

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.True(nextCalled[0]);
        Assert.Null(tenantContext.ResolvedTenantId);
        Assert.Single(audit.Logs, log => log.Action == AuthAuditAction.TenantResolutionFailed);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/auth/service-token")]
    [InlineData("/auth/.well-known/jwks.json")]
    [InlineData("/auth/subdomains/check-availability")]
    [InlineData("/auth/tenant-resolution/by-email")]
    public async Task Exempt_paths_skip_resolution_entirely(string path)
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: true
        );

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("auth-api:8080"); // llamada M2M directa, sin Host de tenant real
        context.Request.Path = path;

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.True(nextCalled[0]);
        Assert.Null(resolver.LastRequestedHost);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task Spoofed_X_Forwarded_Host_header_is_ignored()
    {
        var legitTenantId = Guid.NewGuid();
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: true
        );
        // El resolver SI conoce "legit-tenant.taxproffice.com" — pero el middleware
        // nunca debe preguntarle por ese Host si vino solo en X-Forwarded-Host.
        resolver.ResolveFn = host =>
            host == "legit-tenant.taxproffice.com"
                ? HostResolutionResult.Resolved(legitTenantId)
                : HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("attacker-controlled.invalid");
        context.Request.Headers["X-Forwarded-Host"] = "legit-tenant.taxproffice.com";

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.Equal("attacker-controlled.invalid", resolver.LastRequestedHost);
        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Null(tenantContext.ResolvedTenantId);
        Assert.Single(audit.Logs, log => log.Action == AuthAuditAction.TenantResolutionFailed);
    }

    /// <summary>
    /// El middleware corre en CADA request y publica a un exchange fanout con 17 colas que
    /// persisten en inbox durable: sin deduplicar, un solo host que no resuelve escribe 17 filas
    /// por request en 17 bases de datos distintas. El audit log conserva la cuenta exacta.
    /// </summary>
    [Fact]
    public async Task Repeated_failures_from_the_same_host_are_audited_every_time_but_published_once()
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, _) = BuildMiddleware(
            enforce: false
        );
        resolver.ResolveFn = _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        for (var i = 0; i < 5; i++)
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("localhost");
            await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);
        }

        Assert.Equal(5, audit.Logs.Count(log => log.Action == AuthAuditAction.TenantResolutionFailed));
        Assert.Single(bus.Published.OfType<TenantResolutionFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Each_distinct_host_publishes_its_own_event()
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, _) = BuildMiddleware(
            enforce: false
        );
        resolver.ResolveFn = _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        foreach (var host in new[] { "uno.invalid", "dos.invalid", "tres.invalid" })
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString(host);
            await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);
        }

        Assert.Equal(3, bus.Published.OfType<TenantResolutionFailedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Counter_failure_skips_the_publish_without_breaking_the_request_or_the_audit()
    {
        var (middleware, resolver, tenantContext, audit, unitOfWork, bus, rateCounter, nextCalled) = BuildMiddleware(
            enforce: false
        );
        resolver.ResolveFn = _ => HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);
        rateCounter.ThrowOnIncrement = new InvalidOperationException("Redis caido");

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost");

        await InvokeAsync(middleware, context, resolver, tenantContext, audit, unitOfWork, bus, rateCounter);

        Assert.True(nextCalled[0]);
        Assert.Single(audit.Logs, log => log.Action == AuthAuditAction.TenantResolutionFailed);
        Assert.Empty(bus.Published.OfType<TenantResolutionFailedIntegrationEvent>());
    }
}
