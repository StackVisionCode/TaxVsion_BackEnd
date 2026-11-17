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

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, ":1")]
    public async Task Quote_of_a_renewal_uses_its_own_idempotency_key(int generation, string suffix)
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"code\":\"Codes.X\",\"message\":\"x\"}");
        var client = NewClient(handler);
        var onboardingId = Guid.NewGuid();

        await client.QuoteAsync(
            new TaxVision.Auth.Onboarding.Application.Abstractions.GrowthQuoteRequest(
                "WELCOME20",
                onboardingId,
                Guid.NewGuid(),
                "1",
                12_900,
                "USD",
                "snap",
                86_400,
                generation
            )
        );

        // Una renovación no debe recibir de vuelta la cotización vieja (ya vencida) por idempotencia.
        Assert.Equal($"onb-quote:{onboardingId:N}:snap{suffix}", handler.LastIdempotencyKey);
    }

    [Fact]
    public async Task Quote_returns_the_benefit_and_its_duration()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            "{\"quoteId\":\"8a1c3a7e-2b4d-4e6f-8a9b-0c1d2e3f4a5b\",\"codeDefinitionId\":\"3c2b0f4e-6a1d-4c8e-9f0a-1b2c3d4e5f60\","
                + "\"grossAmountCents\":12900,\"discountAmountCents\":2580,\"netAmountCents\":10320,"
                + "\"currency\":\"USD\",\"expiresAtUtc\":\"2026-12-01T00:00:00Z\",\"benefitType\":\"Percentage\","
                + "\"percentageBasisPoints\":2000,\"fixedAmountCents\":null,\"duration\":\"Months\",\"durationMonths\":3,"
                + "\"codeDisplay\":\"WELC...ME20\"}"
        );

        var result = await NewClient(handler).QuoteAsync(Quote(buyerEmail: "ana@example.com"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(Guid.Parse("3c2b0f4e-6a1d-4c8e-9f0a-1b2c3d4e5f60"), result.Value.CodeDefinitionId);
        Assert.Equal(2_000, result.Value.PercentageBasisPoints);
        Assert.Null(result.Value.FixedAmountCents);
        Assert.Equal("Months", result.Value.Duration);
        Assert.Equal(3, result.Value.DurationMonths);
        Assert.Equal("WELC...ME20", result.Value.CodeDisplay);
    }

    [Fact]
    public async Task Quote_sends_the_buyer_email_so_limits_count_per_person()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"code\":\"Codes.X\",\"message\":\"x\"}");

        await NewClient(handler).QuoteAsync(Quote(buyerEmail: "ana@example.com"));

        Assert.Contains("\"buyerEmail\":\"ana@example.com\"", handler.LastBody);
    }

    [Theory]
    [InlineData("Codes.Buyer.DisposableEmail", "Onboarding.CodeNotAllowedForEmail")]
    [InlineData("Codes.Buyer.InvalidEmail", "Onboarding.CodeNotAllowedForEmail")]
    public async Task A_code_refused_for_the_email_keeps_a_clear_code(string growthCode, string expected)
    {
        var client = NewClient(HttpStatusCode.UnprocessableEntity, $"{{\"code\":\"{growthCode}\",\"message\":\"x\"}}");

        var result = await client.QuoteAsync(Quote(buyerEmail: "ana@mailinator.com"));

        Assert.Equal(expected, result.Error.Code);
    }

    [Fact]
    public async Task A_code_already_used_by_the_same_person_keeps_a_clear_code()
    {
        var client = NewClient(
            HttpStatusCode.Conflict,
            "{\"code\":\"Codes.CodeUsageCounter.SubjectLimitReached\",\"message\":\"x\"}"
        );

        var result = await client.ReserveAsync(Guid.NewGuid(), Guid.NewGuid(), 600, "idem-reserve");

        Assert.Equal("Onboarding.CodeAlreadyUsedByBuyer", result.Error.Code);
    }

    [Fact]
    public async Task Quote_sends_the_plan_its_cycle_and_the_codes_already_applied()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"code\":\"Codes.X\",\"message\":\"x\"}");
        var plan = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var applied = Guid.Parse("66666666-7777-8888-9999-000000000000");

        await NewClient(handler)
            .QuoteAsync(
                Quote(buyerEmail: null) with
                {
                    PlanId = plan,
                    BillingCycle = "Yearly",
                    StackedWithCodeDefinitionIds = [applied],
                }
            );

        Assert.Contains(
            $"\"scopeTargets\":[{{\"type\":\"Plan\",\"scopeId\":\"{plan}\"}},"
                + $"{{\"type\":\"Offer\",\"scopeId\":\"Subscription/{plan}/Yearly\"}}]",
            handler.LastBody
        );
        Assert.Contains($"\"stackedWithCodeDefinitionIds\":[\"{applied}\"]", handler.LastBody);
    }

    [Fact]
    public async Task A_code_that_does_not_stack_keeps_a_clear_code()
    {
        var client = NewClient(
            HttpStatusCode.UnprocessableEntity,
            "{\"code\":\"Codes.CodeDefinition.NotStackable\",\"message\":\"x\"}"
        );

        var result = await client.QuoteAsync(Quote(buyerEmail: null));

        Assert.Equal("Onboarding.CodeNotStackable", result.Error.Code);
    }

    private static TaxVision.Auth.Onboarding.Application.Abstractions.GrowthQuoteRequest Quote(string? buyerEmail) =>
        new("WELCOME20", Guid.NewGuid(), Guid.NewGuid(), "1", 12_900, "USD", "snap", 86_400, BuyerEmail: buyerEmail);

    private static Task<BuildingBlocks.Results.Result> CommitAsync(GrowthOnboardingClient client) =>
        client.CommitAsync(Guid.NewGuid(), Guid.NewGuid(), "snapshot", Guid.NewGuid(), "idem-key");

    private static GrowthOnboardingClient NewClient(HttpStatusCode status, string body) =>
        NewClient(new StubHandler(status, body));

    private static GrowthOnboardingClient NewClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://growth.test/") };
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
        public string? LastIdempotencyKey { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastIdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.FirstOrDefault()
                : null;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
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
