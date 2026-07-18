using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using TaxVision.Connectors.Tests.Providers;

namespace TaxVision.Connectors.Tests.Watch;

public class GraphWatchClientTests
{
    private static GraphWatchClient CreateClient(
        FakeHttpMessageHandler handler,
        string notificationUrl = "https://api.taxprocore.com/connectors/webhooks/graph-notification",
        string clientState = "shared-secret"
    ) =>
        new(
            new HttpClient(handler),
            new FakeOAuthTokenManager(),
            Options.Create(new GraphWatchOptions { NotificationUrl = notificationUrl, ClientState = clientState }),
            NullLogger<GraphWatchClient>.Instance
        );

    [Fact]
    public async Task SetupWatchAsync_ParsesSubscriptionIdAndExpiration()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.Created,
            """{ "id": "sub-123", "expirationDateTime": "2026-07-24T12:00:00Z" }"""
        );

        var result = await CreateClient(handler).SetupWatchAsync(Guid.NewGuid());

        Assert.Equal("sub-123", result.SubscriptionRef);
        Assert.Equal(new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc), result.ExpiresAtUtc);
        Assert.Contains("subscriptions", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task SetupWatchAsync_WithoutConfiguredNotificationUrl_Throws()
    {
        var handler = new FakeHttpMessageHandler();

        await Assert.ThrowsAsync<WatchProviderException>(() =>
            CreateClient(handler, notificationUrl: string.Empty).SetupWatchAsync(Guid.NewGuid())
        );
    }

    [Fact]
    public async Task RenewWatchAsync_PatchesExistingSubscription()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "id": "sub-123", "expirationDateTime": "2026-07-27T12:00:00Z" }""");

        var result = await CreateClient(handler).RenewWatchAsync(Guid.NewGuid(), "sub-123");

        Assert.Equal("sub-123", result.SubscriptionRef);
        Assert.Equal(System.Net.Http.HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Contains("sub-123", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task SetupWatchAsync_WithNonSuccessStatus_Throws()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "");

        await Assert.ThrowsAsync<WatchProviderException>(() => CreateClient(handler).SetupWatchAsync(Guid.NewGuid()));
    }
}
