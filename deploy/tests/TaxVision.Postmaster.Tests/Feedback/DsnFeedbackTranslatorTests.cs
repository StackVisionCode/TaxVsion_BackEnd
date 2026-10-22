using System.Text.Json;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;
using TaxVision.Postmaster.Infrastructure.Feedback.Dsn;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class DsnFeedbackTranslatorTests
{
    private static readonly DsnFeedbackTranslator Translator = new();
    private static readonly Guid AccountId = Guid.NewGuid();

    private static string Report(string action, string? status, string reportId = "report-1") =>
        JsonSerializer.Serialize(
            new ConnectorsDeliveryReportDetectedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                AccountId = AccountId,
                ReportProviderMessageId = reportId,
                OriginalInternetMessageId = "AM6P@outlook.com",
                Recipient = "gone@example.com",
                Action = action,
                StatusCode = status,
                DiagnosticSnippet = "550 5.1.1 user unknown",
                ReportedAtUtc = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc),
            }
        );

    [Theory]
    [InlineData("failed", "5.1.1", DeliveryOutcome.HardBounced)]
    [InlineData("failed", "4.2.2", DeliveryOutcome.SoftBounced)]
    // Sin código no se adivina "permanente": un rebote duro suprime la dirección.
    [InlineData("failed", null, DeliveryOutcome.SoftBounced)]
    [InlineData("delayed", "4.4.1", DeliveryOutcome.Deferred)]
    [InlineData("delivered", "2.0.0", DeliveryOutcome.Delivered)]
    public void Translate_maps_the_report(string action, string? status, DeliveryOutcome expected)
    {
        var item = Assert.Single(Translator.Translate(Report(action, status)).Value);

        Assert.Equal(expected, item.Feedback.Outcome);
        Assert.Equal(FeedbackSource.Dsn, item.Feedback.Source);
        Assert.Equal("AM6P@outlook.com", item.MessageId);
        Assert.Equal("gone@example.com", item.RecipientAddress);
        Assert.Equal("550 5.1.1 user unknown", item.Feedback.Diagnostic);
    }

    [Theory]
    [InlineData("relayed")]
    [InlineData("expanded")]
    public void Translate_skips_reports_that_say_nothing_yet(string action)
    {
        Assert.Empty(Translator.Translate(Report(action, "2.0.0")).Value);
    }

    [Fact]
    public void Translate_gives_the_same_report_the_same_id()
    {
        var first = Assert.Single(Translator.Translate(Report("failed", "5.1.1")).Value);
        var again = Assert.Single(Translator.Translate(Report("failed", "5.1.1")).Value);
        var other = Assert.Single(Translator.Translate(Report("failed", "5.1.1", reportId: "report-2")).Value);

        Assert.Equal(first.Feedback.SourceEventId, again.Feedback.SourceEventId);
        Assert.NotEqual(first.Feedback.SourceEventId, other.Feedback.SourceEventId);
    }

    [Fact]
    public void Translate_fails_on_malformed_payload()
    {
        Assert.Equal("Dsn.MalformedPayload", Translator.Translate("[1,2]").Error.Code);
    }
}
