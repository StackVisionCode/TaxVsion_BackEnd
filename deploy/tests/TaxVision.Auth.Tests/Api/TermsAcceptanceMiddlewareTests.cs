using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using TaxVision.Auth.Api.Middleware;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Tests.Api;

/// <summary>Fase L1.4 / PayFlow Fase 6 — TermsAcceptanceMiddleware: bloquea (409) tenants autenticados que no acepten la version vigente.</summary>
public sealed class TermsAcceptanceMiddlewareTests
{
    private static readonly string ValidHash = new('a', 64);

    private sealed class FakeTenantTermsAcceptanceRepository : ITenantTermsAcceptanceRepository
    {
        public TenantTermsAcceptance? Latest { get; set; }

        public Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Latest);

        public Task<TenantTermsAcceptance?> GetByVersionAsync(
            Guid tenantId,
            Guid userId,
            Guid termsVersionId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        private readonly List<TermsVersion> _all = [];

        public TermsVersion Seed(string version) => Seed(version, DateTime.UtcNow.AddDays(-1));

        public TermsVersion Seed(string version, DateTime effectiveFromUtc)
        {
            var published = TermsVersion
                .Publish(
                    TermsKind.TermsOfService,
                    version,
                    Guid.NewGuid(),
                    ValidHash,
                    "en-US",
                    Guid.NewGuid(),
                    effectiveFromUtc
                )
                .Value;
            _all.Add(published);
            return published;
        }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByContentFileIdAsync(Guid contentFileId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Where(v =>
                        v.Kind == kind
                        && v.Locale == locale
                        && v.EffectiveFromUtc <= nowUtc
                        && (v.EffectiveUntilUtc == null || v.EffectiveUntilUtc > nowUtc)
                    )
                    .OrderByDescending(v => v.EffectiveFromUtc)
                    .FirstOrDefault()
            );
    }

    private static (
        TermsAcceptanceMiddleware middleware,
        FakeTenantTermsAcceptanceRepository acceptances,
        FakeTermsVersionRepository versions,
        bool[] nextCalled
    ) BuildMiddleware(string currentVersion = "2026-07-14")
    {
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        versions.Seed(currentVersion);
        var nextCalled = new bool[1];
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };
        return (new TermsAcceptanceMiddleware(next), acceptances, versions, nextCalled);
    }

    private static Task InvokeAsync(
        TermsAcceptanceMiddleware middleware,
        HttpContext context,
        ITenantTermsAcceptanceRepository acceptances,
        ITermsVersionRepository versions
    ) => middleware.InvokeAsync(context, acceptances, versions);

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
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = new DefaultHttpContext(); // sin User autenticado

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task M2M_tokens_without_a_tenant_id_claim_pass_through()
    {
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("client_id", "signature-worker")], "Test"));

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task Tenant_that_never_accepted_anything_is_blocked_with_409()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_whose_latest_acceptance_is_an_older_version_is_blocked_with_409()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var old = versions.Seed("2025-01-01", DateTime.UtcNow.AddDays(-60));
        acceptances.Latest = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2025-01-01",
            old.Id,
            null,
            "LegacyPreV2",
            null,
            null,
            DateTime.UtcNow
        );
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Tenant_that_accepted_the_current_version_passes_through()
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        acceptances.Latest = TenantTermsAcceptance.Accept(
            tenantId,
            Guid.NewGuid(),
            "2026-07-14",
            Guid.NewGuid(),
            null,
            "ReAcceptance",
            null,
            null,
            DateTime.UtcNow
        );
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task No_published_terms_version_yet_passes_through()
    {
        var tenantId = Guid.NewGuid();
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository(); // sin seed — nadie publico nada todavia
        var nextCalled = new bool[1];
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };
        var middleware = new TermsAcceptanceMiddleware(next);
        var context = AuthenticatedContext(tenantId);

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/auth/service-token")]
    [InlineData("/auth/tenant/terms/accept")]
    [InlineData("/auth/tenant/terms/status")]
    [InlineData("/auth/onboarding/terms/current")]
    public async Task Exempt_paths_skip_the_check_even_for_a_tenant_that_never_accepted(string path)
    {
        var tenantId = Guid.NewGuid();
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = AuthenticatedContext(tenantId);
        context.Request.Path = path;

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    private static HttpContext EnrollmentOnlyContext(Guid tenantId, string method, string path)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(MfaEnrollmentToken.ClaimType, MfaEnrollmentToken.Pending),
            ],
            "Test"
        );
        context.User = new ClaimsPrincipal(identity);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    // BV-04: con el token solo-MFA y términos sin aceptar, configurar el MFA no puede quedar bloqueado por términos.
    [Theory]
    [InlineData("POST", "/auth/mfa/totp/setup")]
    [InlineData("POST", "/auth/mfa/totp/confirm")]
    [InlineData("GET", "/auth/mfa/status")]
    public async Task The_mfa_setup_flow_of_an_enrollment_token_is_not_blocked_by_pending_terms(
        string method,
        string path
    )
    {
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = EnrollmentOnlyContext(Guid.NewGuid(), method, path);

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task A_full_session_still_needs_the_terms_before_the_mfa_endpoints()
    {
        var (middleware, acceptances, versions, nextCalled) = BuildMiddleware();
        var context = AuthenticatedContext(Guid.NewGuid());
        context.Request.Method = "POST";
        context.Request.Path = "/auth/mfa/totp/setup";

        await InvokeAsync(middleware, context, acceptances, versions);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    // BV-04 de punta a punta en el orden real de Program.cs (gate de MFA → gate de términos): el admin sale en
    // cualquier orden, aceptando primero los términos o configurando primero el MFA.
    [Theory]
    [InlineData("POST", "/auth/tenant/terms/accept")]
    [InlineData("GET", "/auth/tenant/terms/status")]
    [InlineData("POST", "/auth/mfa/totp/setup")]
    [InlineData("POST", "/auth/mfa/totp/confirm")]
    public async Task An_enrollment_token_with_pending_terms_is_not_locked_out_either_way(string method, string path)
    {
        var acceptances = new FakeTenantTermsAcceptanceRepository();
        var versions = new FakeTermsVersionRepository();
        versions.Seed("2026-07-14");
        var reached = false;
        var terms = new TermsAcceptanceMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });
        var gate = new MfaEnrollmentGateMiddleware(ctx => terms.InvokeAsync(ctx, acceptances, versions));
        var context = EnrollmentOnlyContext(Guid.NewGuid(), method, path);

        await gate.InvokeAsync(context);

        Assert.True(reached, $"status {context.Response.StatusCode}");
    }
}
