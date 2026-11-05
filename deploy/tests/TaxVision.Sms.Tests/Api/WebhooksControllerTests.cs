using System.Net;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Api.Controllers;
using TaxVision.Sms.Api.RateLimiting;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Api;

/// <summary>Webhook anónimo: un proveedor desconocido no llega al bus y la tasa se limita por IP.</summary>
public sealed class WebhooksControllerTests
{
    [Fact]
    public async Task Unknown_provider_returns_404_without_invoking_the_bus()
    {
        var bus = new FakeMessageBus();
        var invoked = 0;
        bus.OnInvoke = _ =>
        {
            invoked++;
            return Task.FromResult<object?>(Result.Success());
        };
        var controller = BuildController(bus);

        var status = await controller.Status("nope", CancellationToken.None);
        var inbound = await controller.Inbound("nope", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(status);
        Assert.IsType<NotFoundObjectResult>(inbound);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task Known_provider_is_invoked()
    {
        var bus = new FakeMessageBus();
        object? command = null;
        bus.OnInvoke = message =>
        {
            command = message;
            return Task.FromResult<object?>(Result.Success());
        };

        var result = await BuildController(bus).Inbound("fake", CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.IsType<ProcessInboundCommand>(command);
    }

    [Fact]
    public void Webhook_limiter_rejects_above_the_configured_permit_limit_per_ip()
    {
        var options = new SmsWebhookRateLimitOptions { PermitLimit = 2, WindowSeconds = 60 };
        var first = SmsWebhookRateLimiting.Partition(ContextFrom("203.0.113.10"), options);
        var limiter = first.Factory(first.PartitionKey);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.False(limiter.AttemptAcquire().IsAcquired);

        var other = SmsWebhookRateLimiting.Partition(ContextFrom("203.0.113.11"), options);
        Assert.NotEqual(first.PartitionKey, other.PartitionKey);
    }

    private static DefaultHttpContext ContextFrom(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    private static WebhooksController BuildController(FakeMessageBus bus)
    {
        var adapters = new MapSmsAdapterFactory(
            new Dictionary<string, ISmsProvider> { ["fake"] = new FakeSmsProvider() }
        );
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream();
        return new WebhooksController(bus, adapters)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }
}
