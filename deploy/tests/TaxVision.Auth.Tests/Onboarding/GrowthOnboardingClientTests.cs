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
using TaxVision.Auth.Onboarding.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>El commit tardío en revisión llega al finalizador con el código de Growth, no como un fallo genérico.</summary>
public sealed class GrowthOnboardingClientTests
{
    [Fact]
    public async Task Commit_GrowthLateCommitPendingReview_KeepsGrowthCode()
    {
        var client = NewClient(
            HttpStatusCode.BadRequest,
            """{"code":"Codes.CommitReservation.LateCommitPendingReview","message":"Pending manual review."}"""
        );

        var result = await CommitAsync(client);

        Assert.Equal("Codes.CommitReservation.LateCommitPendingReview", result.Error.Code);
    }

    [Fact]
    public async Task Commit_OtherGrowthError_StaysGenericFailure()
    {
        var client = NewClient(
            HttpStatusCode.BadRequest,
            """{"code":"Codes.CommitReservation.SnapshotMismatch","message":"Snapshot mismatch."}"""
        );

        var result = await CommitAsync(client);

        Assert.Equal("Growth.Commit.Failed", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result> CommitAsync(GrowthOnboardingClient client) =>
        client.CommitAsync(Guid.NewGuid(), Guid.NewGuid(), "snapshot", Guid.NewGuid(), "idem-key");

    private static GrowthOnboardingClient NewClient(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("http://growth.test/") };
        var scopes = new ServiceCollection()
            .AddSingleton<IJwtTokenGenerator, DummyTokenGenerator>()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new GrowthOnboardingClient(
            http,
            new ServiceTokenCache(scopes),
            new HttpResiliencePipelineRegistry(),
            NullLogger<GrowthOnboardingClient>.Instance
        );
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
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
        ) => GenerateServiceToken(tenantId, clientId, permissions, lifetimeMinutes);

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
