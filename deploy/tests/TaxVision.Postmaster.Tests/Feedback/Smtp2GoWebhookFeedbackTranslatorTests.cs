using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class Smtp2GoWebhookFeedbackTranslatorTests
{
    private static readonly Smtp2GoWebhookFeedbackTranslator Translator = new(
        NullLogger<Smtp2GoWebhookFeedbackTranslator>.Instance
    );

    /// <summary>Forma de un aviso del webhook (campos de la documentación de SMTP2GO).</summary>
    private static string Event(string eventName, string? bounce = null, string time = "2026-10-21T12:00:00Z") =>
        $$"""
            {
              "event": "{{eventName}}",
              {{(bounce is null ? "" : $"\"bounce\": \"{bounce}\",")}}
              "time": "{{time}}",
              "sendtime": "2026-10-21T11:59:58Z",
              "rcpt": "customer@example.com",
              "email_id": "1oQsWu-4XUKtC-0n",
              "message-id": "<pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>",
              "X-TaxVision-Message-Id": "0f8fad5bd9cb469fa16570867728950e",
              "host": "mx.example.com",
              "message": "550 5.1.1 user unknown",
              "id": "webhook-7"
            }
            """;

    [Theory]
    [InlineData("delivered", null, DeliveryOutcome.Delivered)]
    [InlineData("bounce", "hard", DeliveryOutcome.HardBounced)]
    [InlineData("bounce", "soft", DeliveryOutcome.SoftBounced)]
    // Sin tipo no se adivina "duro": un duro suprime la dirección.
    [InlineData("bounce", null, DeliveryOutcome.SoftBounced)]
    [InlineData("reject", null, DeliveryOutcome.Rejected)]
    [InlineData("spam", null, DeliveryOutcome.Complained)]
    public void Translate_maps_outcomes(string eventName, string? bounce, DeliveryOutcome expected)
    {
        var item = Assert.Single(Translator.Translate(Event(eventName, bounce)).Value);

        Assert.Equal(DeliveryFeedbackKind.Outcome, item.Feedback.Kind);
        Assert.Equal(expected, item.Feedback.Outcome);
        Assert.Equal(FeedbackSource.Smtp2Go, item.Feedback.Source);
    }

    [Theory]
    [InlineData("open", EngagementSignal.Opened)]
    [InlineData("click", EngagementSignal.Clicked)]
    [InlineData("unsubscribe", EngagementSignal.Unsubscribed)]
    public void Translate_maps_signals(string eventName, EngagementSignal expected)
    {
        var item = Assert.Single(Translator.Translate(Event(eventName)).Value);

        Assert.Equal(expected, item.Feedback.Signal);
    }

    [Theory]
    [InlineData("processed")]
    [InlineData("resubscribe")]
    [InlineData("something-new")]
    public void Translate_skips_events_we_do_not_use(string eventName)
    {
        Assert.Empty(Translator.Translate(Event(eventName)).Value);
    }

    [Fact]
    public void Translate_carries_the_three_correlation_ids_and_the_diagnostic()
    {
        var item = Assert.Single(Translator.Translate(Event("bounce", "hard")).Value);

        Assert.Equal("<pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>", item.MessageId);
        Assert.Equal("0f8fad5bd9cb469fa16570867728950e", item.CorrelationHeader);
        Assert.Equal("1oQsWu-4XUKtC-0n", item.ProviderMessageId);
        Assert.Equal("customer@example.com", item.RecipientAddress);
        Assert.Equal("mx.example.com: 550 5.1.1 user unknown", item.Feedback.Diagnostic);
        Assert.Equal(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc), item.Feedback.OccurredAtUtc);
    }

    [Fact]
    public void Translate_gives_the_same_event_the_same_id_so_a_retry_is_a_duplicate()
    {
        var first = Assert.Single(Translator.Translate(Event("delivered")).Value);
        var retry = Assert.Single(Translator.Translate(Event("delivered")).Value);
        var other = Assert.Single(Translator.Translate(Event("delivered", time: "2026-10-21T12:05:00Z")).Value);

        Assert.Equal(first.Feedback.SourceEventId, retry.Feedback.SourceEventId);
        Assert.NotEqual(first.Feedback.SourceEventId, other.Feedback.SourceEventId);
    }

    [Fact]
    public void Translate_reads_unix_time()
    {
        var item = Assert.Single(Translator.Translate(Event("delivered", time: "1792929600")).Value);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792929600).UtcDateTime, item.Feedback.OccurredAtUtc);
    }

    [Fact]
    public void Translate_reads_an_array_of_events()
    {
        var payload = $"[{Event("delivered")},{Event("open")}]";

        Assert.Equal(2, Translator.Translate(payload).Value.Count);
    }

    [Fact]
    public void Translate_skips_an_event_without_recipient()
    {
        Assert.Empty(Translator.Translate("""{"event":"delivered","email_id":"x"}""").Value);
    }

    [Fact]
    public void Translate_fails_on_malformed_json()
    {
        Assert.Equal("Smtp2Go.MalformedPayload", Translator.Translate("{oops").Error.Code);
    }
}
