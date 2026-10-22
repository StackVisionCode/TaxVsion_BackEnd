using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Tenancy;
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

    [Fact]
    public async Task System_email_feedback_is_forwarded_raw_to_Postmaster()
    {
        var body =
            """{"event":"delivered","rcpt":"a@b.com","message-id":"<pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>"}""";
        var (controller, publisher) = CreateControllerWithBody("expected-secret", "expected-secret", body);

        var result = await controller.Smtp2Go(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        var forwarded = Assert.IsType<EmailProviderFeedbackReceivedIntegrationEvent>(
            Assert.Single(publisher.Published)
        );
        Assert.Equal("Smtp2Go", forwarded.Source);
        Assert.Equal(PlatformTenant.Id, forwarded.TenantId);
        // Notification no traduce: el evento viaja entero.
        Assert.Contains("\"rcpt\":\"a@b.com\"", forwarded.RawPayload);
    }

    [Fact]
    public async Task Correlation_header_alone_routes_to_Postmaster()
    {
        var body =
            """{"event":"bounce","message-id":"<rewritten@relay.net>","X-TaxVision-Message-Id":"0f8fad5bd9cb469fa16570867728950e"}""";
        var (controller, publisher) = CreateControllerWithBody("expected-secret", "expected-secret", body);

        await controller.Smtp2Go(CancellationToken.None);

        Assert.IsType<EmailProviderFeedbackReceivedIntegrationEvent>(Assert.Single(publisher.Published));
    }

    [Fact]
    public async Task Unknown_traffic_is_ignored_and_counted()
    {
        var metrics = new RecordingMetrics();
        var (controller, publisher) = CreateControllerWithBody(
            "expected-secret",
            "expected-secret",
            """{"event":"delivered","message-id":"<CAJxyz@mail.gmail.com>"}""",
            metrics: metrics
        );

        var result = await controller.Smtp2Go(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(publisher.Published);
        Assert.Equal(1, metrics.Unrouted);
    }

    [Fact]
    public async Task Each_event_of_an_array_goes_to_its_owner()
    {
        var campaignId = CampaignEmailCorrelation.BuildMessageId(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "d-1");
        var body =
            "[{\"event\":\"delivered\",\"message-id\":\"<"
            + campaignId
            + ">\"},"
            + "{\"event\":\"delivered\",\"message-id\":\"<pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>\"}]";
        var (controller, publisher) = CreateControllerWithBody("expected-secret", "expected-secret", body);

        await controller.Smtp2Go(CancellationToken.None);

        Assert.Single(publisher.Published.OfType<CampaignDispatchResultIntegrationEvent>());
        Assert.Single(publisher.Published.OfType<EmailProviderFeedbackReceivedIntegrationEvent>());
    }

    [Fact]
    public async Task Form_encoded_payload_is_forwarded_as_json()
    {
        var (controller, publisher) = CreateControllerWithBody(
            "expected-secret",
            "expected-secret",
            "event=delivered&rcpt=a%40b.com&message-id=%3Cpm.0f8fad5bd9cb469fa16570867728950e%40taxoffix.com%3E",
            "application/x-www-form-urlencoded"
        );

        await controller.Smtp2Go(CancellationToken.None);

        var forwarded = Assert.IsType<EmailProviderFeedbackReceivedIntegrationEvent>(
            Assert.Single(publisher.Published)
        );
        Assert.Contains("\"event\":\"delivered\"", forwarded.RawPayload);
    }

    [Fact]
    public async Task Unexpected_content_type_is_rejected()
    {
        var (controller, publisher) = CreateControllerWithBody(
            "expected-secret",
            "expected-secret",
            "hello",
            "text/plain"
        );

        var result = await controller.Smtp2Go(CancellationToken.None);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Malformed_json_is_a_bad_request()
    {
        var (controller, _) = CreateControllerWithBody("expected-secret", "expected-secret", "{not json");

        Assert.IsType<BadRequestResult>(await controller.Smtp2Go(CancellationToken.None));
    }

    [Theory]
    [InlineData("<a.b.c.d@campaign.notification.local>", false)]
    [InlineData(
        "0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.d-1@evil.com",
        false
    )]
    [InlineData(
        "0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.d-1",
        false
    )]
    [InlineData(
        "0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.0f8fad5bd9cb469fa16570867728950e.d-1@campaign.notification.local",
        true
    )]
    public void Campaign_correlation_requires_its_own_domain(string messageId, bool expected)
    {
        // Antes cualquier id de 4 partes pasaba como campaña, sin mirar el dominio.
        Assert.Equal(expected, CampaignEmailCorrelation.TryParse(messageId, out _, out _, out _, out _));
    }

    private static (CampaignEmailWebhookController Controller, RecordingPublisher Publisher) CreateController(
        string? configuredSecret,
        string? providedSecret,
        string? messageId = null
    ) =>
        CreateControllerWithBody(
            configuredSecret,
            providedSecret,
            JsonSerializer.Serialize(
                new Dictionary<string, string>
                {
                    ["event"] = "delivered",
                    ["message-id"] = messageId ?? "not-a-campaign-message",
                }
            )
        );

    private static (CampaignEmailWebhookController Controller, RecordingPublisher Publisher) CreateControllerWithBody(
        string? configuredSecret,
        string? providedSecret,
        string body,
        string contentType = "application/json",
        RecordingMetrics? metrics = null
    )
    {
        var values = new Dictionary<string, string?> { ["CampaignEmail:WebhookSecret"] = configuredSecret };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var publisher = new RecordingPublisher();
        var controller = new CampaignEmailWebhookController(
            publisher,
            metrics ?? new RecordingMetrics(),
            configuration,
            NullLogger<CampaignEmailWebhookController>.Instance
        );
        var context = new DefaultHttpContext();
        if (providedSecret is not null)
            context.Request.Headers.Authorization = $"Bearer {providedSecret}";

        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return (controller, publisher);
    }

    private sealed class RecordingMetrics : IProviderWebhookMetrics
    {
        public int Unrouted { get; private set; }

        public void RecordUnrouted(string source) => Unrouted++;
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
