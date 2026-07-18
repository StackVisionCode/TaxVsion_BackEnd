using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using TaxVision.Connectors.Infrastructure.RateLimit;
using TaxVision.Connectors.Tests.OAuth;
using ImapProviderClient = TaxVision.Connectors.Infrastructure.Providers.Imap.ImapClient;

namespace TaxVision.Connectors.Tests.Providers;

public class EmailProviderClientFactoryTests
{
    private static EmailProviderClientFactory CreateFactory()
    {
        var rateLimiter = new NoWaitProviderRateLimiter();
        var tokenManager = new FakeOAuthTokenManager();
        var circuitBreakers = new ProviderCircuitBreakerRegistry(NullLogger<ProviderCircuitBreakerRegistry>.Instance);
        var gmail = new GmailApiClient(
            new HttpClient(new FakeHttpMessageHandler()),
            tokenManager,
            rateLimiter,
            circuitBreakers,
            NullLogger<GmailApiClient>.Instance
        );
        var graph = new GraphApiClient(
            new HttpClient(new FakeHttpMessageHandler()),
            tokenManager,
            rateLimiter,
            circuitBreakers,
            NullLogger<GraphApiClient>.Instance
        );
        var imap = new ImapProviderClient(
            new FakeImapCredentialsRepository(),
            new FakeEncryptedSecretProtector(),
            rateLimiter,
            circuitBreakers,
            NullLogger<ImapProviderClient>.Instance
        );
        return new EmailProviderClientFactory(gmail, graph, imap);
    }

    [Fact]
    public void Resolve_WithGmail_ReturnsGmailClient()
    {
        var result = CreateFactory().Resolve(ProviderCode.Gmail);

        Assert.True(result.IsSuccess);
        Assert.IsType<GmailApiClient>(result.Value);
    }

    [Fact]
    public void Resolve_WithGraph_ReturnsGraphClient()
    {
        var result = CreateFactory().Resolve(ProviderCode.Graph);

        Assert.True(result.IsSuccess);
        Assert.IsType<GraphApiClient>(result.Value);
    }

    [Fact]
    public void Resolve_WithImap_ReturnsImapClient()
    {
        var result = CreateFactory().Resolve(ProviderCode.Imap);

        Assert.True(result.IsSuccess);
        Assert.IsType<ImapProviderClient>(result.Value);
    }
}
