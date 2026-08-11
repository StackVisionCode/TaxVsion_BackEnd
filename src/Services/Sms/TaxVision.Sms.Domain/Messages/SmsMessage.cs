using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Domain.Messages;

/// <summary>Entrada de media ya validada (VO) para construir un <see cref="SmsMessage"/>.</summary>
public sealed record SmsMediaInput(string Url, string ContentType, string? FileName, long? SizeBytes);

/// <summary>
/// Un intento independiente de envío de SMS/MMS. Aggregate root, tenant-owned. Los datos de identidad
/// (<see cref="CustomerId"/>, <see cref="To"/>, <see cref="Body"/>, media) son un SNAPSHOT inmutable del
/// intento — nunca se mutan, aunque el customer cambie de teléfono luego. Provider- y domain-agnóstico:
/// <see cref="SourceContext"/> es un string opaco (nunca genera lógica ni FK a otro dominio).
/// </summary>
public sealed class SmsMessage : TenantEntity
{
    private readonly List<SmsMedia> _media = [];

    public Guid CustomerId { get; private set; }
    public string To { get; private set; } = default!;
    public string Body { get; private set; } = default!;

    public string IdempotencyKey { get; private set; } = default!;
    public string CorrelationId { get; private set; } = default!;
    public Guid BatchId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public string? SourceContext { get; private set; }

    public SmsMessageStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SmsMedia> Media => _media;

    private SmsMessage() { }

    public static Result<SmsMessage> Create(
        Guid tenantId,
        Guid customerId,
        PhoneE164 to,
        SmsBody body,
        string idempotencyKey,
        string correlationId,
        Guid batchId,
        string providerCode,
        string? sourceContext,
        IReadOnlyList<SmsMediaInput> media,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SmsMessage>(SmsErrors.InvalidTenant);
        if (customerId == Guid.Empty)
            return Result.Failure<SmsMessage>(SmsErrors.InvalidCustomer);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<SmsMessage>(SmsErrors.InvalidIdempotencyKey);

        var message = new SmsMessage
        {
            CustomerId = customerId,
            To = to.Value,
            Body = body.Value,
            IdempotencyKey = idempotencyKey.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim(),
            BatchId = batchId,
            ProviderCode = providerCode,
            SourceContext = string.IsNullOrWhiteSpace(sourceContext) ? null : sourceContext.Trim(),
            Status = SmsMessageStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        message.SetTenant(tenantId);

        foreach (var m in media)
            message._media.Add(new SmsMedia(message.Id, m.Url, m.ContentType, m.FileName, m.SizeBytes, nowUtc));

        return Result.Success(message);
    }

    /// <summary>El proveedor aceptó el envío (aún sin confirmación de entrega). Idempotente.</summary>
    public Result MarkAccepted(string providerMessageId, DateTime nowUtc)
    {
        if (Status == SmsMessageStatus.Accepted)
            return Result.Success();
        if (Status != SmsMessageStatus.Pending)
            return Result.Failure(SmsErrors.InvalidTransition);

        ProviderMessageId = providerMessageId;
        Status = SmsMessageStatus.Accepted;
        AcceptedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>DLR de entrega confirmada. Idempotente frente a replays del webhook.</summary>
    public Result MarkDelivered(DateTime nowUtc)
    {
        if (Status == SmsMessageStatus.Delivered)
            return Result.Success();
        if (Status is not (SmsMessageStatus.Accepted or SmsMessageStatus.Pending))
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Delivered;
        DeliveredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Fallo terminal (rechazo del proveedor o DLR fallido). Idempotente.</summary>
    public Result MarkFailed(DateTime nowUtc, string? code, string? reason)
    {
        if (Status == SmsMessageStatus.Failed)
            return Result.Success();
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.Accepted))
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Failed;
        FailureCode = code;
        FailureReason = reason;
        FailedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Número inválido / no entregable (DLR permanente). Idempotente.</summary>
    public Result MarkUndeliverable(DateTime nowUtc, string? code, string? reason)
    {
        if (Status == SmsMessageStatus.Undeliverable)
            return Result.Success();
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.Accepted))
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Undeliverable;
        FailureCode = code;
        FailureReason = reason;
        FailedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Suprimido por opt-out (el destinatario hizo STOP): NO se envía al proveedor.</summary>
    public Result MarkSuppressed(string reason, DateTime nowUtc)
    {
        if (Status == SmsMessageStatus.Suppressed)
            return Result.Success();
        if (Status != SmsMessageStatus.Pending)
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Suppressed;
        FailureCode = "suppressed";
        FailureReason = reason;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
