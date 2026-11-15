using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback;
using TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

namespace TaxVision.Postmaster.Tests.Feedback;

/// <summary>
/// El poller de SMTP2GO se registra siempre; sin API key no se ofrece y la consulta de respaldo queda apagada
/// (el webhook sigue funcionando).
/// </summary>
public sealed class DeliveryFeedbackCapabilitiesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Without_an_api_key_there_is_no_pull(string? apiKey)
    {
        var capabilities = Capabilities(apiKey);

        Assert.False(capabilities.SupportsPull(FeedbackSource.Smtp2Go));
        Assert.Null(capabilities.FindPoller(FeedbackSource.Smtp2Go));
        Assert.Null(capabilities.FindEngagementPoller(FeedbackSource.Smtp2Go));
        Assert.True(capabilities.SupportsPush(FeedbackSource.Smtp2Go));
    }

    [Fact]
    public void With_an_api_key_both_pollers_are_offered()
    {
        var capabilities = Capabilities("api-test-key");

        Assert.True(capabilities.SupportsPull(FeedbackSource.Smtp2Go));
        Assert.NotNull(capabilities.FindPoller(FeedbackSource.Smtp2Go));
        Assert.NotNull(capabilities.FindEngagementPoller(FeedbackSource.Smtp2Go));
    }

    private static DeliveryFeedbackCapabilities Capabilities(string? apiKey)
    {
        var poller = new Smtp2GoActivityFeedbackPoller(
            new HttpClient { BaseAddress = new Uri("https://api.smtp2go.com/v3/") },
            Options.Create(new Smtp2GoFeedbackOptions { ApiKey = apiKey }),
            NullLogger<Smtp2GoActivityFeedbackPoller>.Instance
        );
        return new DeliveryFeedbackCapabilities(
            [new Smtp2GoWebhookFeedbackTranslator(NullLogger<Smtp2GoWebhookFeedbackTranslator>.Instance)],
            [poller],
            [poller]
        );
    }
}
