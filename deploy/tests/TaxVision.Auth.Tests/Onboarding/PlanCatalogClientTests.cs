using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Onboarding.Infrastructure.HttpClients;

namespace TaxVision.Auth.Tests.Onboarding;

// El recibo nombra el plan aunque ya no se venda: se pide por id al endpoint interno, no al listado público.
public sealed class PlanCatalogClientTests
{
    [Fact]
    public async Task Asks_the_internal_endpoint_with_a_service_token()
    {
        var planId = Guid.NewGuid();
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"id":"{{planId}}","code":"pro","name":"Pro"}""");

        var name = await NewClient(handler).GetPlanNameAsync(planId);

        Assert.Equal("Pro", name);
        Assert.Equal($"/internal/plans/{planId}", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task An_unknown_plan_has_no_name()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"code":"Subscription.Plan.NotFound"}""");

        Assert.Null(await NewClient(handler).GetPlanNameAsync(Guid.NewGuid()));
    }

    private static PlanCatalogClient NewClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://subscription.test/") };
        var scopes = new ServiceCollection()
            .AddSingleton<IJwtTokenGenerator, DummyTokenGenerator>()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new PlanCatalogClient(
            http,
            new ServiceTokenCache(scopes),
            new HttpResiliencePipelineRegistry(),
            NullLogger<PlanCatalogClient>.Instance
        );
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }

    private sealed class DummyTokenGenerator : IJwtTokenGenerator
    {
        public AccessToken GenerateScopedServiceToken(
            Guid tenantId,
            string clientId,
            IReadOnlyCollection<string> permissions,
            IReadOnlyCollection<string> scopes,
            string audience,
            int lifetimeMinutes
        ) => new("dummy", 300);

        public AccessToken GenerateServiceToken(
            Guid tenantId,
            string clientId,
            IReadOnlyCollection<string> permissions,
            int lifetimeMinutes
        ) => new("dummy", 300);

        public AccessToken Generate(
            User user,
            string effectiveTimeZoneId,
            Guid sessionId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            SessionSurface surface = SessionSurface.Workspace,
            DateTime? reauthenticatedAtUtc = null,
            bool mfaEnrollmentOnly = false
        ) => throw new NotSupportedException();

        public AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc) =>
            throw new NotSupportedException();
    }
}
