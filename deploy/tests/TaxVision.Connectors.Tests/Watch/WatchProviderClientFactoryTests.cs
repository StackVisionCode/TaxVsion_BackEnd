using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using TaxVision.Connectors.Tests.Providers;

namespace TaxVision.Connectors.Tests.Watch;

public class WatchProviderClientFactoryTests
{
    private static WatchProviderClientFactory CreateFactory()
    {
        var gmail = new GmailWatchClient(
            new HttpClient(new FakeHttpMessageHandler()),
            new FakeOAuthTokenManager(),
            Options.Create(new GmailWatchOptions { TopicName = "projects/tv/topics/gmail-push" }),
            NullLogger<GmailWatchClient>.Instance
        );
        var graph = new GraphWatchClient(
            new HttpClient(new FakeHttpMessageHandler()),
            new FakeOAuthTokenManager(),
            Options.Create(
                new GraphWatchOptions { NotificationUrl = "https://api.taxprocore.com/x", ClientState = "s" }
            ),
            NullLogger<GraphWatchClient>.Instance
        );
        return new WatchProviderClientFactory(gmail, graph);
    }

    [Fact]
    public void Resolve_WithGmail_ReturnsGmailClient()
    {
        var result = CreateFactory().Resolve(ProviderCode.Gmail);

        Assert.True(result.IsSuccess);
        Assert.IsType<GmailWatchClient>(result.Value);
    }

    [Fact]
    public void Resolve_WithGraph_ReturnsGraphClient()
    {
        var result = CreateFactory().Resolve(ProviderCode.Graph);

        Assert.True(result.IsSuccess);
        Assert.IsType<GraphWatchClient>(result.Value);
    }

    [Fact]
    public void Resolve_WithImap_Fails()
    {
        var result = CreateFactory().Resolve(ProviderCode.Imap);

        Assert.True(result.IsFailure);
        Assert.Equal("WatchProviderClientFactory.NotSupported", result.Error.Code);
    }
}
