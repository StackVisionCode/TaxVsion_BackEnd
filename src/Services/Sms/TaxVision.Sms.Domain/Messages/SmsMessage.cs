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
    /// <summary>Motivo de supresión cuando el cliente apagó los SMS en su política de contacto.</summary>
    public const string ContactPolicySuppressionCode = "contact_policy";

    private readonly List<SmsMedia> _media = [];

    public Guid CustomerId { get; private set; }
    public string To { get; private set; } = default!;

    /// <summary>Nombre del destinatario AL MOMENTO del envío (snapshot, como el resto del mensaje).
    /// Lo aporta el caller (el CRM lo tiene del picker); null para envíos de sistema/M2M. No se usa
    /// para lógica — solo para mostrar en el log sin depender de Customer al leer.</summary>
    public string? RecipientName { get; private set; }
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

    /// <summary>Momento en que se reservó la fila para enviar. Null en filas que nunca salieron al proveedor.</summary>
    public DateTime? SendAttemptedAtUtc { get; private set; }

    /// <summary>El lote se cobra al Wallet (envío individual). La conciliación liquida solo estos lotes.</summary>
    public bool WalletCharged { get; private set; }

    /// <summary>Concurrencia optimista: el webhook, la conciliación y el registro del envío escriben la misma fila.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Envío pedido sin resultado final todavía (en curso o desconocido).</summary>
    public bool IsUnresolved => Status is SmsMessageStatus.Pending or SmsMessageStatus.OutcomeUnknown;

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
        DateTime nowUtc,
        string? recipientName = null,
        bool walletCharged = false
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
            RecipientName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName.Trim(),
            Body = body.Value,
            IdempotencyKey = idempotencyKey.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId.Trim(),
            BatchId = batchId,
            ProviderCode = providerCode,
            SourceContext = string.IsNullOrWhiteSpace(sourceContext) ? null : sourceContext.Trim(),
            Status = SmsMessageStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            WalletCharged = walletCharged,
        };
        message.SetTenant(tenantId);

        foreach (var m in media)
            message._media.Add(new SmsMedia(message.Id, m.Url, m.ContentType, m.FileName, m.SizeBytes, nowUtc));

        return Result.Success(message);
    }

    /// <summary>La fila queda reservada para enviar (fase 1). Solo en <see cref="SmsMessageStatus.Pending"/>.</summary>
    public Result MarkSendAttempted(DateTime nowUtc)
    {
        if (Status != SmsMessageStatus.Pending)
            return Result.Failure(SmsErrors.InvalidTransition);

        SendAttemptedAtUtc ??= nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>No se sabe si el proveedor lo envió (timeout después de pedirlo). Idempotente.</summary>
    public Result MarkOutcomeUnknown(
        DateTime nowUtc,
        string? providerMessageId,
        string code,
        string? reason,
        string? providerCode = null
    )
    {
        if (Status == SmsMessageStatus.OutcomeUnknown)
            return Result.Success();
        if (Status != SmsMessageStatus.Pending)
            return Result.Failure(SmsErrors.InvalidTransition);

        if (!string.IsNullOrWhiteSpace(providerCode))
            ProviderCode = providerCode;
        ProviderMessageId = providerMessageId;
        Status = SmsMessageStatus.OutcomeUnknown;
        FailureCode = code;
        FailureReason = reason;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>El proveedor aceptó el envío (aún sin confirmación de entrega). Idempotente.
    /// <paramref name="providerCode"/> registra el proveedor que lo envió cuando hubo failover.</summary>
    public Result MarkAccepted(string providerMessageId, DateTime nowUtc, string? providerCode = null)
    {
        if (Status == SmsMessageStatus.Accepted)
            return Result.Success();
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.OutcomeUnknown))
            return Result.Failure(SmsErrors.InvalidTransition);

        if (!string.IsNullOrWhiteSpace(providerCode))
            ProviderCode = providerCode;
        ProviderMessageId = providerMessageId;
        FailureCode = null;
        FailureReason = null;
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
        if (Status is not (SmsMessageStatus.Accepted or SmsMessageStatus.Pending or SmsMessageStatus.OutcomeUnknown))
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
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.Accepted or SmsMessageStatus.OutcomeUnknown))
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
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.Accepted or SmsMessageStatus.OutcomeUnknown))
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Undeliverable;
        FailureCode = code;
        FailureReason = reason;
        FailedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Suprimido (STOP o política de contacto del cliente): NO se envía al proveedor.</summary>
    public Result MarkSuppressed(string reason, DateTime nowUtc, string code = "suppressed")
    {
        if (Status == SmsMessageStatus.Suppressed)
            return Result.Success();
        if (Status != SmsMessageStatus.Pending)
            return Result.Failure(SmsErrors.InvalidTransition);

        Status = SmsMessageStatus.Suppressed;
        FailureCode = code;
        FailureReason = reason;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
