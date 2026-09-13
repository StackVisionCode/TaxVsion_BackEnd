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
        Guid tenantId,
        PaymentProviderCode providerCode,
        string providerEventId,
        string eventType,
        string rawPayload,
        string signatureHeader,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WebhookEvent>(new Error("WebhookEvent.InvalidTenant", "TenantId is required."));

        if (string.IsNullOrWhiteSpace(providerEventId))
            return Result.Failure<WebhookEvent>(
                new Error("WebhookEvent.InvalidProviderEventId", "ProviderEventId is required.")
            );

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

    /// <summary>
    /// Un evento en estado terminal ya fue resuelto: reintentos posteriores del provider son
    /// duplicados que deben descartarse. Los NO terminales (Received/Processing/Failed) quedaron a
    /// medias — un fallo transitorio a mitad de proceso — y una nueva entrega debe re-procesarlos.
    /// </summary>
    public bool IsTerminal =>
        Status
            is WebhookEventStatus.Applied
                or WebhookEventStatus.Rejected
                or WebhookEventStatus.Duplicate
                or WebhookEventStatus.Stale;

    public Result MarkProcessing(DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Received)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot process from {Status}."));

        Status = WebhookEventStatus.Processing;
        return Result.Success();
    }

    /// <summary>
    /// Re-conduce a Processing un evento NO terminal ante una entrega repetida del provider: la fila
    /// se registró (idempotencia por unique index) pero un fallo transitorio la dejó sin aplicar (p.ej.
    /// Failed). El re-proceso es idempotente a nivel de TenantPayment. Los estados terminales nunca
    /// llegan acá — el caller los corta antes con <see cref="IsTerminal"/>.
    /// </summary>
    public Result MarkReprocessing(DateTime nowUtc)
    {
        if (IsTerminal)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot reprocess from {Status}."));

        Status = WebhookEventStatus.Processing;
        ProcessingError = null;
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

    public Result MarkStale(Guid? relatedTenantPaymentId, string reason, DateTime nowUtc)
    {
        if (Status != WebhookEventStatus.Processing)
            return Result.Failure(new Error("WebhookEvent.InvalidTransition", $"Cannot mark stale from {Status}."));

        Status = WebhookEventStatus.Stale;
        RelatedTenantPaymentId = relatedTenantPaymentId;
        ProcessingError = reason;
        ProcessedAtUtc = nowUtc;
        return Result.Success();
    }
}
