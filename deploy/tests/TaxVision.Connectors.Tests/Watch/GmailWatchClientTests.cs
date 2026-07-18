using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using TaxVision.Connectors.Tests.Providers;

namespace TaxVision.Connectors.Tests.Watch;

public class GmailWatchClientTests
{
    private static GmailWatchClient CreateClient(
        FakeHttpMessageHandler handler,
        string topicName = "projects/tv/topics/gmail-push"
    ) =>
        new(
            new HttpClient(handler),
            new FakeOAuthTokenManager(),
            Options.Create(new GmailWatchOptions { TopicName = topicName }),
            NullLogger<GmailWatchClient>.Instance
        );

    [Fact]
    public async Task SetupWatchAsync_ParsesHistoryIdAndExpiration()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "historyId": "12345", "expiration": "1700000000000" }""");

        var result = await CreateClient(handler).SetupWatchAsync(Guid.NewGuid());

        Assert.Equal("12345", result.SubscriptionRef);
        Assert.Equal("projects/tv/topics/gmail-push", result.TopicName);
        Assert.Contains("users/me/watch", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task SetupWatchAsync_WithoutConfiguredTopic_Throws()
    {
        var handler = new FakeHttpMessageHandler();

        await Assert.ThrowsAsync<WatchProviderException>(() =>
            CreateClient(handler, topicName: string.Empty).SetupWatchAsync(Guid.NewGuid())
        );
    }

    [Fact]
    public async Task SetupWatchAsync_WithNonSuccessStatus_Throws()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<WatchProviderException>(() => CreateClient(handler).SetupWatchAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RenewWatchAsync_CallsSameEndpointAsSetup()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "historyId": "99999", "expiration": "1700000000000" }""");

        var result = await CreateClient(handler).RenewWatchAsync(Guid.NewGuid(), "unused-ref");

        Assert.Equal("99999", result.SubscriptionRef);
        Assert.Contains("users/me/watch", handler.Requests[0].RequestUri!.ToString());
    }
}
