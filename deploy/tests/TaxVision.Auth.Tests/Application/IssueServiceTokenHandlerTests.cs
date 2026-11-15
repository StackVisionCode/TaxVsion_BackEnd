using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.ServiceTokens;
using TaxVision.Auth.Application.ServiceTokens.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

public sealed class IssueServiceTokenHandlerTests
{
    [Fact]
    public async Task Configured_audience_and_scopes_are_forwarded_to_the_token_generator()
    {
        var generator = new CapturingTokenGenerator();
        var options = Options.Create(
            new ServiceAuthOptions
            {
                TokenLifetimeMinutes = 7,
                Clients =
                [
                    new ServiceClientConfig
                    {
                        ClientId = "payment-app",
                        Secret = "test-secret",
                        Audience = "taxvision-growth",
                        Permissions = ["codes.code.read"],
                        Scopes = ["growth.codes.quote", "growth.codes.reserve"],
                    },
                ],
            }
        );

        Result<ServiceTokenResponse> result = await IssueServiceTokenHandler.Handle(
            new IssueServiceTokenCommand("payment-app", "test-secret", Guid.NewGuid()),
            generator,
            options,
            new RecordingThrottler(),
            new FixedRequest("10.0.0.5"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("taxvision-growth", generator.Audience);
        Assert.Equal(["growth.codes.quote", "growth.codes.reserve"], generator.Scopes);
        Assert.Equal(7, generator.LifetimeMinutes);
    }

    [Fact]
    public async Task A_wrong_secret_counts_as_a_failure_for_the_caller_address()
    {
        var throttler = new RecordingThrottler();

        var result = await Issue(new IssueServiceTokenCommand("payment-app", "guess", Guid.NewGuid()), throttler);

        Assert.Equal("Auth.InvalidClient", result.Error.Code);
        Assert.Equal(["10.0.0.5"], throttler.Failures);
    }

    [Fact]
    public async Task An_unknown_client_counts_as_a_failure_with_the_same_answer()
    {
        var throttler = new RecordingThrottler();

        var result = await Issue(new IssueServiceTokenCommand("nobody", "guess", Guid.NewGuid()), throttler);

        Assert.Equal("Auth.InvalidClient", result.Error.Code);
        Assert.Single(throttler.Failures);
    }

    [Fact]
    public async Task A_valid_client_is_not_counted()
    {
        var throttler = new RecordingThrottler();

        var result = await Issue(new IssueServiceTokenCommand("payment-app", "test-secret", Guid.NewGuid()), throttler);

        Assert.True(result.IsSuccess);
        Assert.Empty(throttler.Failures);
    }

    /// <summary>Bloqueada la dirección, ni siquiera se evalúa el secreto: el que adivina no aprende nada.</summary>
    [Fact]
    public async Task A_blocked_address_is_refused_before_the_credentials_are_checked()
    {
        var generator = new CapturingTokenGenerator();
        var throttler = new RecordingThrottler { RetryAfter = TimeSpan.FromMinutes(9) };

        var result = await Issue(
            new IssueServiceTokenCommand("payment-app", "test-secret", Guid.NewGuid()),
            throttler,
            generator
        );

        Assert.Equal("Auth.LockedOut", result.Error.Code);
        Assert.Equal(540, result.Error.RetryAfterSeconds);
        Assert.Null(generator.Audience);
        Assert.Empty(throttler.Failures);
    }

    private static Task<Result<ServiceTokenResponse>> Issue(
        IssueServiceTokenCommand command,
        RecordingThrottler throttler,
        CapturingTokenGenerator? generator = null
    ) =>
        IssueServiceTokenHandler.Handle(
            command,
            generator ?? new CapturingTokenGenerator(),
            Options.Create(
                new ServiceAuthOptions
                {
                    Clients =
                    [
                        new ServiceClientConfig
                        {
                            ClientId = "payment-app",
                            Secret = "test-secret",
                            Audience = "taxvision-growth",
                        },
                    ],
                }
            ),
            throttler,
            new FixedRequest("10.0.0.5"),
            CancellationToken.None
        );

    private sealed class RecordingThrottler : IServiceClientThrottler
    {
        public TimeSpan? RetryAfter { get; init; }
        public List<string?> Failures { get; } = [];

        public Task<TimeSpan?> GetRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.FromResult(RetryAfter);

        public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default)
        {
            Failures.Add(ipAddress);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRequest(string ipAddress) : IRequestContext
    {
        public string? IpAddress => ipAddress;
        public string? UserAgent => "test";
    }

    private sealed class CapturingTokenGenerator : IJwtTokenGenerator
    {
        public string? Audience { get; private set; }
        public IReadOnlyCollection<string> Scopes { get; private set; } = [];
        public int LifetimeMinutes { get; private set; }

        public AccessToken Generate(
            User user,
            string effectiveTimeZoneId,
            Guid sessionId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            TaxVision.Auth.Domain.RefreshTokens.SessionSurface surface =
                TaxVision.Auth.Domain.RefreshTokens.SessionSurface.Workspace,
            DateTime? reauthenticatedAtUtc = null,
            bool mfaEnrollmentOnly = false
        ) => throw new NotSupportedException();

        public AccessToken GenerateServiceToken(
            Guid tenantId,
            string clientId,
            IReadOnlyCollection<string> permissions,
            int lifetimeMinutes
        ) => new("legacy", lifetimeMinutes * 60);

        public AccessToken GenerateScopedServiceToken(
            Guid tenantId,
            string clientId,
            IReadOnlyCollection<string> permissions,
            IReadOnlyCollection<string> scopes,
            string audience,
            int lifetimeMinutes
        )
        {
            Audience = audience;
            Scopes = scopes;
            LifetimeMinutes = lifetimeMinutes;
            return new AccessToken("scoped", lifetimeMinutes * 60);
        }

        public AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc) =>
            throw new NotSupportedException();
    }
}
