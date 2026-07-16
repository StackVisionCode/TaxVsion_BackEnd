using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Webhooks;

/// <summary>
/// Registro append-only de un evento entrante del provider. Único por
/// <c>(TenantId, ProviderCode, ProviderEventId)</c> — a diferencia de PaymentApp, acá el
/// tenant SÍ se conoce desde el principio (viene en el path del webhook,
/// <c>/payments-client/webhooks/{tenantId}/stripe</c>, porque hace falta saber de qué tenant
/// es para elegir qué webhook secret usar al verificar la firma).
/// </summary>
public sealed class WebhookEvent : TenantEntity
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
    public Guid? RelatedTenantPaymentId { get; private set; }

    private WebhookEvent() { }

    public static Result<WebhookEvent> Receive(
        Guid tenantId, PaymentProviderCode providerCode, string providerEventId, string eventType, string rawPayload, string signatureHeader, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WebhookEvent>(new Error("WebhookEvent.InvalidTenant", "TenantId is required."));

        if (string.IsNullOrWhiteSpace(providerEventId))
            return Result.Failure<WebhookEvent>(new Error("WebhookEvent.InvalidProviderEventId", "ProviderEventId is required."));

        if (string.IsNullOrWhiteSpace(eventType))
            return Result.Failure<WebhookEvent>(new Error("WebhookEvent.InvalidEventType", "EventType is required."));

        var webhookEvent = new WebhookEvent
        {
            ProviderCode = providerCode,
            ProviderEventId = providerEventId,
            EventType = eventType,
            ReceivedAtUtc = nowUtc,
            RawPayload = rawPayload,
            SignatureHeader = signatureHeader,
            Status = WebhookEventStatus.Received,
        };
        webhookEvent.SetTenant(tenantId);
        return Result.Success(webhookEvent);
    }

    public Result MarkProcessing(DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Received)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot process from {Status}."));

        Status = WebhookEventStatus.Processing;
        return Result.Success();
    }

    public Result MarkApplied(Guid? relatedTenantPaymentId, DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Processing)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot apply from {Status}."));

        Status = WebhookEventStatus.Applied;
        RelatedTenantPaymentId = relatedTenantPaymentId;
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
}
