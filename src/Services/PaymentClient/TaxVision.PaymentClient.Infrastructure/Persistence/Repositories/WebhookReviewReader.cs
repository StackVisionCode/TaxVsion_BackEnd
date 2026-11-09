using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Admin.Queries;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

/// <summary>Webhooks NeedsReview de todas las oficinas, con el pago y la factura relacionados.</summary>
public sealed class WebhookReviewReader(PaymentClientDbContext db) : IWebhookReviewReader
{
    private const int MaxRows = 500;

    public async Task<IReadOnlyList<WebhookNeedingReviewResponse>> ListNeedingReviewAsync(
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters: consulta admin cross-tenant (mismo criterio que SearchAdminAsync).
        var events = await db
            .WebhookEvents.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(w => w.Status == WebhookEventStatus.NeedsReview)
            .OrderBy(w => w.ReceivedAtUtc)
            .Take(MaxRows)
            .ToListAsync(ct);
        if (events.Count == 0)
            return [];

        var paymentIds = events
            .Where(w => w.RelatedTenantPaymentId != null)
            .Select(w => w.RelatedTenantPaymentId!.Value)
            .ToList();
        var payments = await db
            .TenantPayments.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return events
            .Select(w =>
            {
                var payment = w.RelatedTenantPaymentId is { } id ? payments.GetValueOrDefault(id) : null;
                return new WebhookNeedingReviewResponse(
                    w.Id,
                    w.TenantId,
                    w.ProviderCode.ToString(),
                    w.ProviderEventId,
                    w.EventType,
                    w.ProcessingError,
                    w.ReceivedAtUtc,
                    w.ProcessedAtUtc,
                    w.RelatedTenantPaymentId,
                    payment?.Status.ToString(),
                    payment?.Amount.AmountCents,
                    payment?.Amount.Currency,
                    payment?.Purpose.Kind.ToString(),
                    payment?.Purpose.ExternalReferenceId,
                    payment?.ExternalChargeReference?.Value
                );
            })
            .ToList();
    }
}
