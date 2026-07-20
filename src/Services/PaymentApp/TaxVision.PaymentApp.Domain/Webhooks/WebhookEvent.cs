using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.Webhooks;

/// <summary>
/// Registro append-only de un evento entrante del provider. Único por
/// <c>(ProviderCode, ProviderEventId)</c> — el dedupe real lo garantiza el unique index en
/// Infrastructure (guardrail: la factory no consulta la BD); un segundo intento de insertar
/// el mismo evento falla con <c>ConflictException</c> y el caller lo trata como duplicado.
/// No hereda <see cref="TenantEntity"/>: el tenant no siempre se conoce al recibir el
/// webhook — se resuelve después, vía el <see cref="SaaSPayments.SaaSPayment"/> relacionado
/// (§42 del diseño).
/// </summary>
public sealed class WebhookEvent : BaseEntity
{
    public PaymentProviderCode ProviderCode { get; private set; }
    public string ProviderEventId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public DateTime ReceivedAtUtc { get; private set; }
    public string RawPayload { get; private set; } = default!;
    public string SignatureHeader { get; private set; } = default!;
    public WebhookEventStatus Status { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public string? ProcessingError { get; private set; }
    public Guid? RelatedSaaSPaymentId { get; private set; }

    private WebhookEvent() { }

    public static Result<WebhookEvent> Receive(
        PaymentProviderCode providerCode,
        string providerEventId,
        string eventType,
        string rawPayload,
        string signatureHeader,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
            return Result.Failure<WebhookEvent>(
                new Error("WebhookEvent.InvalidProviderEventId", "ProviderEventId is required.")
            );

        if (string.IsNullOrWhiteSpace(eventType))
            return Result.Failure<WebhookEvent>(new Error("WebhookEvent.InvalidEventType", "EventType is required."));

        return Result.Success(
            new WebhookEvent
            {
                ProviderCode = providerCode,
                ProviderEventId = providerEventId,
                EventType = eventType,
                ReceivedAtUtc = nowUtc,
                RawPayload = rawPayload,
                SignatureHeader = signatureHeader,
                Status = WebhookEventStatus.Received,
            }
        );
    }

    public Result MarkProcessing(DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Received)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot process from {Status}."));

        Status = WebhookEventStatus.Processing;
        return Result.Success();
    }

    public Result MarkApplied(Guid? relatedSaaSPaymentId, DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Processing)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot apply from {Status}."));

        Status = WebhookEventStatus.Applied;
        RelatedSaaSPaymentId = relatedSaaSPaymentId;
        ProcessedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkRejected(string reason, DateTime nowUtc)
    {
        if (Status is WebhookEventStatus.Applied or WebhookEventStatus.Duplicate)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot reject from {Status}."));

        Status = WebhookEventStatus.Rejected;
        ProcessingError = reason;
        ProcessedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkFailed(string error, DateTime nowUtc)
    {
        if (Status is WebhookEventStatus.Applied or WebhookEventStatus.Duplicate)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot fail from {Status}."));

        Status = WebhookEventStatus.Failed;
        ProcessingError = error;
        ProcessedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkStale(Guid? relatedSaaSPaymentId, string reason, DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Processing)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot mark stale from {Status}."));

        Status = WebhookEventStatus.Stale;
        RelatedSaaSPaymentId = relatedSaaSPaymentId;
        ProcessingError = reason;
        ProcessedAtUtc = nowUtc;
        return Result.Success();
    }
}
