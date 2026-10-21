using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Api.Controllers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Api;

public sealed class GraphNotificationWebhookControllerTests
{
    private const string ClientState = "shared-secret";

    [Fact]
    public async Task Notification_IsQueuedAndAcknowledgedWithoutProcessingInline()
    {
        // Graph espera la respuesta en ~3 s: el controller solo encola, nunca corre el sync.
        var bus = new FakeMessageBus();
        var controller = CreateController(
            bus,
            $$"""
            { "value": [
                { "subscriptionId": "sub-1", "clientState": "{{ClientState}}" },
                { "subscriptionId": "sub-2", "clientState": "{{ClientState}}" }
            ] }
            """
        );

        var result = await controller.HandleNotification(null, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(bus.Invoked);
        var queued = bus.Published.Cast<GraphNotificationReceived>().ToList();
        Assert.Equal(["sub-1", "sub-2"], queued.Select(m => m.SubscriptionId));
        Assert.All(queued, m => Assert.Equal("webhook-correlation", m.CorrelationId));
    }

    [Fact]
    public async Task Notification_WithMismatchedClientState_IsNotQueued()
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(
            bus,
            """{ "value": [ { "subscriptionId": "sub-1", "clientState": "forged" } ] }"""
        );

        var result = await controller.HandleNotification(null, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData("reauthorizationRequired")]
    [InlineData("subscriptionRemoved")]
    [InlineData("missed")]
    public async Task LifecycleNotification_IsQueuedWithItsEvent(string lifecycleEvent)
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(
            bus,
            $$"""
            { "value": [ {
                "subscriptionId": "sub-1",
                "clientState": "{{ClientState}}",
                "lifecycleEvent": "{{lifecycleEvent}}",
                "subscriptionExpirationDateTime": "2026-10-23T12:00:00Z"
            } ] }
            """
        );

        var result = await controller.HandleLifecycleNotification(null, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        var queued = Assert.IsType<GraphLifecycleNotificationReceived>(Assert.Single(bus.Published));
        Assert.Equal("sub-1", queued.SubscriptionId);
        Assert.Equal(lifecycleEvent, queued.LifecycleEvent);
    }

    [Fact]
    public async Task LifecycleNotification_WithoutLifecycleEvent_IsNotQueued()
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(
            bus,
            $$"""{ "value": [ { "subscriptionId": "sub-1", "clientState": "{{ClientState}}" } ] }"""
        );

        await controller.HandleLifecycleNotification(null, CancellationToken.None);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task LifecycleNotification_AnswersTheValidationHandshake()
    {
        // Graph valida también la lifecycleNotificationUrl al crear la subscription.
        var controller = CreateController(new FakeMessageBus(), "");

        var result = await controller.HandleLifecycleNotification("token-123", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("token-123", content.Content);
        Assert.Equal("text/plain", content.ContentType);
    }

    private static GraphNotificationWebhookController CreateController(FakeMessageBus bus, string body)
    {
        var correlation = new FakeCorrelationContext();
        correlation.Set("webhook-correlation");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return new GraphNotificationWebhookController(
            bus,
            Options.Create(new GraphWatchOptions { ClientState = ClientState }),
            correlation,
            NullLogger<GraphNotificationWebhookController>.Instance
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }
}
