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
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("taxvision-growth", generator.Audience);
        Assert.Equal(["growth.codes.quote", "growth.codes.reserve"], generator.Scopes);
        Assert.Equal(7, generator.LifetimeMinutes);
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
            IReadOnlyCollection<string> permissions,
            IReadOnlyCollection<string> authMethods
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
