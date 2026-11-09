namespace TaxVision.PaymentClient.Application.Admin.Queries;

/// <summary>Bandeja cross-tenant de éxitos del proveedor que no se aplicaron (PCL-4). Solo lectura.</summary>
public sealed record ListWebhooksNeedingReviewQuery;

public sealed record WebhookNeedingReviewResponse(
    Guid WebhookEventId,
    Guid TenantId,
    string ProviderCode,
    string ProviderEventId,
    string EventType,
    string? Reason,
    DateTime ReceivedAtUtc,
    DateTime? ProcessedAtUtc,
    Guid? TenantPaymentId,
    string? PaymentStatus,
    long? AmountCents,
    string? Currency,
    string? PurposeKind,
    string? InvoiceReference,
    string? ExternalChargeReference
);

public interface IWebhookReviewReader
{
    Task<IReadOnlyList<WebhookNeedingReviewResponse>> ListNeedingReviewAsync(CancellationToken ct = default);
}
