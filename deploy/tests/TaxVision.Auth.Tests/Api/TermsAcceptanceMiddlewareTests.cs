using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Middleware;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Terms;
using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Tests.Api;

/// <summary>Fase L1.4 — TermsAcceptanceMiddleware: bloquea (409) tenants autenticados que no acepten la version vigente.</summary>
public sealed class TermsAcceptanceMiddlewareTests
{
    private sealed class FakeTenantTermsAcceptanceRepository : ITenantTermsAcceptanceRepository
    {
        public TenantTermsAcceptance? Latest { get; set; }

        public Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Latest);
    }

    private static (
        TermsAcceptanceMiddleware middleware,
        FakeTenantTermsAcceptanceRepository acceptances,
        bool[] nextCalled
    ) BuildMiddleware(string currentVersion = "2026-07-14")
    {
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var nextCalled = new bool[1];
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };
        return (new TermsAcceptanceMiddleware(next), acceptances, nextCalled);
    }

    private static Task InvokeAsync(
        TermsAcceptanceMiddleware middleware,
        HttpContext context,
        ITenantTermsAcceptanceRepository acceptances,
        string currentVersion = "2026-07-14"
    ) =>
        middleware.InvokeAsync(
            context,
            acceptances,
            Options.Create(new TermsOptions { CurrentVersion = currentVersion })
        );

    private static HttpContext AuthenticatedContext(Guid tenantId)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "Test");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    [Fact]
    public async Task Unauthenticated_requests_pass_through_without_checking_acceptance()
    {
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        var context = new DefaultHttpContext(); // sin User autenticado

        await InvokeAsync(middleware, context, acceptances);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task M2M_tokens_without_a_tenant_id_claim_pass_through()
    {
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", "signature-worker")], "Test"));

        await InvokeAsync(middleware, context, acceptances);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task Tenant_that_never_accepted_anything_is_blocked_with_409()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_whose_latest_acceptance_is_an_older_version_is_blocked_with_409()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        acceptances.Latest = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2025-01-01",
            null,
            null,
            DateTime.UtcNow
        );
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_that_accepted_the_current_version_passes_through()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        acceptances.Latest = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2026-07-14",
            null,
            null,
            DateTime.UtcNow
        );
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances);

        Assert.True(nextCalled[0]);
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/auth/service-token")]
    [InlineData("/auth/tenant/terms/accept")]
    [InlineData("/auth/tenant/terms/status")]
    public async Task Exempt_paths_skip_the_check_even_for_a_tenant_that_never_accepted(string path)
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, nextCalled) = BuildMiddleware();
        var context = AuthenticatedContext(tenantId);
        context.Request.Path = path;

        await InvokeAsync(middleware, context, acceptances);

        Assert.True(nextCalled[0]);
    }
}
