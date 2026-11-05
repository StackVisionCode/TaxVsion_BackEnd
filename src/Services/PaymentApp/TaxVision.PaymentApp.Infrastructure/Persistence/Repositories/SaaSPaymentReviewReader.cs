using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Admin.Queries;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

/// <summary>Bandeja de revisión cross-tenant: pagos marcados y, si lo hay, el webhook que quedó NeedsReview.</summary>
public sealed class SaaSPaymentReviewReader(PaymentAppDbContext db) : ISaaSPaymentReviewReader
{
    private const int MaxRows = 500;

    public async Task<IReadOnlyList<SaaSPaymentReviewResponse>> ListAsync(bool resolved, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: consulta admin cross-tenant (mismo criterio que SearchAdminAsync).
        var flagged = db.SaaSPayments.AsNoTracking().IgnoreQueryFilters().Where(p => p.ReviewRequiredAtUtc != null);
        flagged = resolved
            ? flagged.Where(p => p.ReviewResolvedAtUtc != null)
            : flagged.Where(p => p.ReviewResolvedAtUtc == null);

        var payments = await flagged.OrderBy(p => p.ReviewRequiredAtUtc).Take(MaxRows).ToListAsync(ct);
        if (payments.Count == 0)
            return [];

        var ids = payments.Select(p => p.Id).ToList();
        var webhooks = await db
            .WebhookEvents.AsNoTracking()
            .Where(w => w.RelatedSaaSPaymentId != null && ids.Contains(w.RelatedSaaSPaymentId.Value))
            .Where(w => w.Status == WebhookEventStatus.NeedsReview)
            .ToListAsync(ct);
        var webhookByPayment = webhooks
            .GroupBy(w => w.RelatedSaaSPaymentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.ReceivedAtUtc).First());

        return payments
            .Select(p =>
            {
                webhookByPayment.TryGetValue(p.Id, out var webhook);
                return new SaaSPaymentReviewResponse(
                    p.Id,
                    p.TenantId,
                    p.Type.ToString(),
                    p.Status.ToString(),
                    p.Amount.AmountCents,
                    p.Amount.Currency,
                    p.ProviderCode.ToString(),
                    p.ExternalChargeReference?.Value,
                    p.ProviderCheckoutSessionId,
                    p.ReviewRequiredAtUtc!.Value,
                    p.ReviewReasonCode!,
                    p.ReviewProviderReference,
                    p.ReviewPaidAmountCents,
                    p.ReviewPaidCurrency,
                    p.ReviewResolvedAtUtc,
                    p.ReviewResolvedByUserId,
                    p.ReviewResolution,
                    webhook is null
                        ? null
                        : new SaaSPaymentReviewWebhookResponse(
                            webhook.Id,
                            webhook.ProviderEventId,
                            webhook.EventType,
                            webhook.ReceivedAtUtc,
                            webhook.ProcessingError
                        )
                );
            })
            .ToList();
    }
}
