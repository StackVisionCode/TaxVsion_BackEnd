namespace TaxVision.Sms.Application.Providers;

/// <summary>Matriz declarativa de capacidades del proveedor, consultada ANTES de despachar.</summary>
public sealed record SmsProviderCapabilities
{
    public required bool SupportsDeliveryReceipts { get; init; }
    public required bool SupportsInbound { get; init; }
    public required bool SupportsBulkSend { get; init; }
    public required int MaxBatchSize { get; init; }

    public required bool SupportsMedia { get; init; }
    public required bool SupportsMultipleMedia { get; init; }
    public required int MaxMediaItems { get; init; }
    public required long MaxMediaSizeBytes { get; init; }

    /// <summary>MIME types soportados; vacío = sin restricción específica del adapter.</summary>
    public required IReadOnlySet<string> AllowedMediaTypes { get; init; }
}

/// <summary>Media canónica de un envío (referencia, nunca bytes).</summary>
public sealed record SmsMediaPayload(string Url, string ContentType, string? FileName, long? SizeBytes);

/// <summary>Request canónico interno que el adapter transforma al formato de su proveedor.</summary>
public sealed record SmsSendRequest(
    Guid TenantId,
    Guid CustomerId,
    string To,
    string Body,
    IReadOnlyList<SmsMediaPayload> Media,
    string CorrelationId,
    string IdempotencyKey,
    string? SourceContext
);

/// <summary>Resultado de un envío. <see cref="ProviderMessageId"/> liga el DLR posterior con este envío.</summary>
public sealed record SmsSendResult(bool Accepted, string? ProviderMessageId, string? ErrorCode, string? ErrorMessage);

/// <summary>Resultado de verificar la firma del webhook.</summary>
public sealed record SmsSignatureCheck(bool IsValid, string? Reason);

/// <summary>Estados canónicos a los que un adapter mapea el estado externo del proveedor.</summary>
public enum SmsCanonicalStatus
{
    Accepted,
    Delivered,
    Failed,
    Undeliverable,
}

/// <summary>DLR/estado del proveedor ya normalizado.</summary>
public sealed record SmsDeliveryUpdate(
    string ProviderMessageId,
    string EventType,
    SmsCanonicalStatus Status,
    string? FailureCode,
    string? FailureReason
);

/// <summary>Palabras clave de opt-out estándar.</summary>
public enum SmsInboundKeyword
{
    Stop,
    Start,
    Help,
    Unknown,
}

/// <summary>Mensaje inbound del proveedor ya normalizado (STOP/START/HELP). El teléfono viene del
/// proveedor; el adapter puede incluir hints de tenant/customer si el proveedor los devuelve (metadata).</summary>
public sealed record SmsInboundMessage(
    string FromPhone,
    SmsInboundKeyword Keyword,
    string RawKeyword,
    string EventType,
    string ProviderMessageId,
    Guid? TenantIdHint,
    Guid? CustomerIdHint
);
