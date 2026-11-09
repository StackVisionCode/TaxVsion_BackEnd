using TaxVision.PaymentClient.Application.Admin.Queries;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>La bandeja de plataforma lista solo los webhooks NeedsReview, con el pago y la factura.</summary>
public sealed class ListWebhooksNeedingReviewHandlerTests
{
    [Fact]
    public async Task Lists_only_needs_review_events_with_payment_and_invoice()
    {
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        await using var db = InMemoryPaymentClient.CreateDb();
        var payment = TenantPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"charge-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-42").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_late").Value,
            null,
            null,
            Guid.Empty,
            nowUtc
        );
        payment.MarkFailed("card_declined", "Declined.", false, null, Guid.Empty, nowUtc);
        db.TenantPayments.Add(payment);
        var review = Event(tenantId, "evt_review", nowUtc);
        review.MarkNeedsReview(payment.Id, "TenantPayment.InvalidTransition", nowUtc);
        var stale = Event(tenantId, "evt_stale", nowUtc);
        stale.MarkStale(payment.Id, "TenantPayment.InvalidTransition", nowUtc);
        db.WebhookEvents.AddRange(review, stale);
        await db.SaveChangesAsync();

        var result = await ListWebhooksNeedingReviewHandler.Handle(
            new ListWebhooksNeedingReviewQuery(),
            new WebhookReviewReader(db),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal("evt_review", row.ProviderEventId);
        Assert.Equal("TenantPayment.InvalidTransition", row.Reason);
        Assert.Equal(payment.Id, row.TenantPaymentId);
        Assert.Equal("Failed", row.PaymentStatus);
        Assert.Equal(4900, row.AmountCents);
        Assert.Equal("inv-42", row.InvoiceReference);
    }

    private static WebhookEvent Event(Guid tenantId, string providerEventId, DateTime nowUtc)
    {
        var evt = WebhookEvent
            .Receive(
                tenantId,
                PaymentProviderCode.Stripe,
                providerEventId,
                "payment_intent.succeeded",
                "{}",
                "sig",
                nowUtc
            )
            .Value;
        evt.MarkProcessing(nowUtc);
        return evt;
    }
}
