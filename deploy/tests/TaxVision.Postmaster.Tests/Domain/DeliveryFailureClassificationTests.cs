using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class DeliveryFailureClassificationTests
{
    [Theory]
    [InlineData("5.1.1", DeliveryFailureCategory.InvalidAddress)]
    [InlineData("5.1.10", DeliveryFailureCategory.InvalidAddress)]
    [InlineData("5.1.0", DeliveryFailureCategory.InvalidAddress)]
    [InlineData("5.1.3", DeliveryFailureCategory.InvalidAddress)]
    [InlineData("5.1.6", DeliveryFailureCategory.InvalidAddress)]
    [InlineData("5.1.2", DeliveryFailureCategory.DomainNotFound)]
    [InlineData("5.4.4", DeliveryFailureCategory.DomainNotFound)]
    [InlineData("5.4.310", DeliveryFailureCategory.DomainNotFound)]
    // DNS caído no prueba que el dominio no exista.
    [InlineData("5.4.3", DeliveryFailureCategory.Other)]
    [InlineData("5.2.2", DeliveryFailureCategory.MailboxFull)]
    [InlineData("4.2.2", DeliveryFailureCategory.MailboxFull)]
    [InlineData("5.7.1", DeliveryFailureCategory.Rejected)]
    [InlineData("5.7.26", DeliveryFailureCategory.Rejected)]
    [InlineData("5.3.4", DeliveryFailureCategory.TooLarge)]
    [InlineData("5.2.3", DeliveryFailureCategory.TooLarge)]
    [InlineData("5.1.8", DeliveryFailureCategory.Other)]
    [InlineData("5.0.0", DeliveryFailureCategory.Other)]
    // Falló tras reintentos temporales: no es "sigue intentando".
    [InlineData("4.4.7", DeliveryFailureCategory.Other)]
    [InlineData(null, DeliveryFailureCategory.Other)]
    public void Classify_maps_a_failure_by_its_status_code(string? code, DeliveryFailureCategory expected)
    {
        var status = EnhancedStatusCode.Find(code);

        Assert.Equal(expected, DeliveryFailureClassification.Classify(DeliveryOutcome.HardBounced, status));
        Assert.Equal(expected, DeliveryFailureClassification.Classify(DeliveryOutcome.SoftBounced, status));
    }

    [Theory]
    [InlineData("4.4.1", DeliveryFailureCategory.Delayed)]
    [InlineData(null, DeliveryFailureCategory.Delayed)]
    [InlineData("4.2.2", DeliveryFailureCategory.MailboxFull)]
    public void Classify_a_delay(string? code, DeliveryFailureCategory expected)
    {
        Assert.Equal(
            expected,
            DeliveryFailureClassification.Classify(DeliveryOutcome.Deferred, EnhancedStatusCode.Find(code))
        );
    }

    [Fact]
    public void Classify_a_provider_rejection_without_code_as_rejected()
    {
        Assert.Equal(
            DeliveryFailureCategory.Rejected,
            DeliveryFailureClassification.Classify(DeliveryOutcome.Rejected, null)
        );
    }

    [Theory]
    [InlineData(DeliveryOutcome.Delivered)]
    [InlineData(DeliveryOutcome.Accepted)]
    [InlineData(DeliveryOutcome.Complained)]
    public void Classify_has_nothing_to_explain_when_the_email_arrived(DeliveryOutcome outcome)
    {
        Assert.Null(DeliveryFailureClassification.Classify(outcome, EnhancedStatusCode.Find("5.1.1")));
    }

    [Theory]
    [InlineData("5.2.2", DeliveryOutcome.SoftBounced)]
    [InlineData("5.1.1", DeliveryOutcome.HardBounced)]
    [InlineData(null, DeliveryOutcome.HardBounced)]
    public void Normalize_turns_a_full_mailbox_into_a_soft_bounce(string? code, DeliveryOutcome expected)
    {
        Assert.Equal(
            expected,
            DeliveryFailureClassification.Normalize(DeliveryOutcome.HardBounced, EnhancedStatusCode.Find(code))
        );
    }

    [Fact]
    public void ForOutcome_reads_the_code_from_the_diagnostic_and_never_keeps_a_full_mailbox_as_hard()
    {
        var feedback = DeliveryFeedback
            .ForOutcome(
                DeliveryOutcome.HardBounced,
                DateTime.UtcNow,
                FeedbackSource.Smtp2Go,
                "evt-1",
                "mx.outlook.com: 552 5.2.2 mailbox full"
            )
            .Value;

        Assert.Equal(DeliveryOutcome.SoftBounced, feedback.Outcome);
        Assert.Equal("5.2.2", feedback.StatusCode?.ToString());
        Assert.Equal(DeliveryFailureCategory.MailboxFull, feedback.FailureCategory);
    }

    [Fact]
    public void ForOutcome_prefers_the_separate_status_code_over_the_diagnostic()
    {
        var feedback = DeliveryFeedback
            .ForOutcome(DeliveryOutcome.HardBounced, DateTime.UtcNow, FeedbackSource.Dsn, "evt-1", "550 5.7.1", "5.1.1")
            .Value;

        Assert.Equal("5.1.1", feedback.StatusCode?.ToString());
        Assert.Equal(DeliveryFailureCategory.InvalidAddress, feedback.FailureCategory);
    }
}
