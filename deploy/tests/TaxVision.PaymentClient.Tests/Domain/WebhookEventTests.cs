using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Tests.Domain;

/// <summary>
/// Idempotencia status-aware (hardening F1): <see cref="WebhookEvent.IsTerminal"/> distingue los
/// eventos ya resueltos (se descartan en un reintento) de los que quedaron a medias, y
/// <see cref="WebhookEvent.MarkReprocessing"/> re-conduce estos últimos a Processing.
/// </summary>
public sealed class WebhookEventTests
{
    private static WebhookEvent Received() =>
        WebhookEvent
            .Receive(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "evt_1",
                "payment_intent.succeeded",
                "{}",
                "signature-snapshot",
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Applied_rejected_and_stale_are_terminal()
    {
        var applied = Received();
        applied.MarkProcessing(DateTime.UtcNow);
        applied.MarkApplied(null, DateTime.UtcNow);
        Assert.True(applied.IsTerminal);

        var rejected = Received();
        rejected.MarkRejected("unknown charge", DateTime.UtcNow);
        Assert.True(rejected.IsTerminal);

        var stale = Received();
        stale.MarkProcessing(DateTime.UtcNow);
        stale.MarkStale(null, "stale", DateTime.UtcNow);
        Assert.True(stale.IsTerminal);
    }

    [Fact]
    public void Received_processing_and_failed_are_not_terminal()
    {
        Assert.False(Received().IsTerminal);

        var processing = Received();
        processing.MarkProcessing(DateTime.UtcNow);
        Assert.False(processing.IsTerminal);

        var failed = Received();
        failed.MarkFailed("transient", DateTime.UtcNow);
        Assert.False(failed.IsTerminal);
    }

    [Fact]
    public void MarkReprocessing_redrives_a_failed_event_back_to_processing_and_clears_the_error()
    {
        var evt = Received();
        evt.MarkFailed("transient failure on a previous delivery", DateTime.UtcNow);

        var result = evt.MarkReprocessing(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(WebhookEventStatus.Processing, evt.Status);
        Assert.Null(evt.ProcessingError);
    }

    [Fact]
    public void MarkReprocessing_is_rejected_for_a_terminal_event()
    {
        var evt = Received();
        evt.MarkProcessing(DateTime.UtcNow);
        evt.MarkApplied(null, DateTime.UtcNow);

        var result = evt.MarkReprocessing(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("WebhookEvent.InvalidTransition", result.Error.Code);
        Assert.Equal(WebhookEventStatus.Applied, evt.Status);
    }
}
