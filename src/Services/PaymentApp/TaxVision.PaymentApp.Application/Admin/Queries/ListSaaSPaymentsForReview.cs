using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Admin.Queries;

/// <summary>Bandeja admin de pagos con un éxito del proveedor que no se aplicó (cross-tenant).</summary>
public sealed record ListSaaSPaymentsForReviewQuery(bool Resolved);

public sealed record SaaSPaymentReviewWebhookResponse(
    Guid Id,
    string ProviderEventId,
    string EventType,
    DateTime ReceivedAtUtc,
    string? ProcessingError
);

public sealed record SaaSPaymentReviewResponse(
    Guid PaymentId,
    Guid TenantId,
    string Type,
    string Status,
    long AmountCents,
    string Currency,
    string ProviderCode,
    string? ExternalChargeReference,
    string? ProviderCheckoutSessionId,
    DateTime ReviewRequiredAtUtc,
    string ReviewReasonCode,
    string? ReviewProviderReference,
    long? ReviewPaidAmountCents,
    string? ReviewPaidCurrency,
    DateTime? ReviewResolvedAtUtc,
    Guid? ReviewResolvedByUserId,
    string? ReviewResolution,
    SaaSPaymentReviewWebhookResponse? WebhookEvent
);

/// <summary>Lectura de la bandeja de revisión; la implementa Infrastructure (une pagos y webhooks).</summary>
public interface ISaaSPaymentReviewReader
{
    Task<IReadOnlyList<SaaSPaymentReviewResponse>> ListAsync(bool resolved, CancellationToken ct = default);
}

public static class ListSaaSPaymentsForReviewHandler
{
    public static async Task<Result<IReadOnlyList<SaaSPaymentReviewResponse>>> Handle(
        ListSaaSPaymentsForReviewQuery query,
        ISaaSPaymentReviewReader reader,
        CancellationToken ct
    ) => Result.Success(await reader.ListAsync(query.Resolved, ct));
}
