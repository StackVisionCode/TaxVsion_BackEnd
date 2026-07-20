using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class WebhookEventTests
{
    [Fact]
    public void Receive_with_an_empty_tenant_id_fails()
    {
        var result = WebhookEvent.Receive(
            Guid.Empty,
            PaymentProviderCode.Stripe,
            "evt_123",
            "payment_intent.succeeded",
            "{}",
            "sig",
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("WebhookEvent.InvalidTenant", result.Error.Code);
    }

    [Fact]
    public void Receive_with_an_empty_provider_event_id_fails()
    {
        var result = WebhookEvent.Receive(
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            "  ",
            "payment_intent.succeeded",
            "{}",
            "sig",
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("WebhookEvent.InvalidProviderEventId", result.Error.Code);
    }

    [Fact]
    public void Receive_starts_in_Received_status()
    {
        var webhookEvent = CreateReceivedEvent();

        Assert.Equal(WebhookEventStatus.Received, webhookEvent.Status);
    }

    [Fact]
    public void MarkProcessing_then_MarkApplied_reaches_Applied_with_the_related_payment()
    {
        var webhookEvent = CreateReceivedEvent();
        var relatedPaymentId = Guid.NewGuid();

        var processing = webhookEvent.MarkProcessing(DateTime.UtcNow);
        var applied = webhookEvent.MarkApplied(relatedPaymentId, DateTime.UtcNow);

        Assert.True(processing.IsSuccess);
        Assert.True(applied.IsSuccess);
        Assert.Equal(WebhookEventStatus.Applied, webhookEvent.Status);
        Assert.Equal(relatedPaymentId, webhookEvent.RelatedTenantPaymentId);
    }

    [Fact]
    public void MarkApplied_can_never_be_undone_by_a_later_reject_or_fail()
    {
        var webhookEvent = CreateReceivedEvent();
        webhookEvent.MarkProcessing(DateTime.UtcNow);
        webhookEvent.MarkApplied(Guid.NewGuid(), DateTime.UtcNow);

        var rejected = webhookEvent.MarkRejected("late", DateTime.UtcNow);
        var failed = webhookEvent.MarkFailed("late", DateTime.UtcNow);

        Assert.True(rejected.IsFailure);
        Assert.True(failed.IsFailure);
        Assert.Equal(WebhookEventStatus.Applied, webhookEvent.Status);
    }

    [Fact]
    public void MarkStale_from_processing_records_the_payment_and_reason()
    {
        var webhookEvent = CreateReceivedEvent();
        var relatedPaymentId = Guid.NewGuid();
        webhookEvent.MarkProcessing(DateTime.UtcNow);

        var result = webhookEvent.MarkStale(relatedPaymentId, "TenantPayment.InvalidState", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(WebhookEventStatus.Stale, webhookEvent.Status);
        Assert.Equal(relatedPaymentId, webhookEvent.RelatedTenantPaymentId);
        Assert.Equal("TenantPayment.InvalidState", webhookEvent.ProcessingError);
    }

    private static WebhookEvent CreateReceivedEvent() =>
        WebhookEvent
            .Receive(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "evt_123",
                "payment_intent.succeeded",
                "{}",
                "t=1,v1=abc",
                DateTime.UtcNow
            )
            .Value;
}
