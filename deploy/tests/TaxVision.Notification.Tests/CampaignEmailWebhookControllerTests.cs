using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Api.Controllers;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Tests;

public sealed class CampaignEmailWebhookControllerTests
{
    [Fact]
    public async Task Missing_configured_secret_fails_closed_without_publishing()
    {
        var (controller, publisher) = CreateController(configuredSecret: null, providedSecret: null);

        var result = await controller.Smtp2Go(CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Invalid_secret_is_unauthorized_without_publishing()
    {
        var (controller, publisher) = CreateController("expected-secret", "wrong-secret");

        var result = await controller.Smtp2Go(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Valid_bearer_secret_publishes_campaign_result()
    {
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var messageId = CampaignEmailCorrelation.BuildMessageId(tenantId, campaignId, runId, "dispatch-1");
        var (controller, publisher) = CreateController("expected-secret", "expected-secret", messageId);

        var result = await controller.Smtp2Go(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        var published = Assert.IsType<CampaignDispatchResultIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(tenantId, published.TenantId);
        Assert.Equal(campaignId, published.CampaignId);
        Assert.Equal(runId, published.RunId);
        Assert.Equal("dispatch-1", published.DispatchId);
        Assert.Equal("Delivered", published.Outcome);
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "different", false)]
    [InlineData("secret", null, false)]
    public void Secret_comparison_is_exact_and_constant_time_backed(string expected, string? provided, bool matches)
    {
        Assert.Equal(matches, CampaignWebhookSecretVerifier.Matches(expected, provided));
    }

    private static (CampaignEmailWebhookController Controller, RecordingPublisher Publisher) CreateController(
        string? configuredSecret,
        string? providedSecret,
        string? messageId = null
    )
    {
        var values = new Dictionary<string, string?> { ["CampaignEmail:WebhookSecret"] = configuredSecret };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var publisher = new RecordingPublisher();
        var controller = new CampaignEmailWebhookController(
            publisher,
            configuration,
            NullLogger<CampaignEmailWebhookController>.Instance
        );
        var context = new DefaultHttpContext();
        if (providedSecret is not null)
            context.Request.Headers.Authorization = $"Bearer {providedSecret}";

        var body = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["event"] = "delivered",
                ["message-id"] = messageId ?? "not-a-campaign-message",
            }
        );
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return (controller, publisher);
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public ConcurrentBag<object> Published { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}
