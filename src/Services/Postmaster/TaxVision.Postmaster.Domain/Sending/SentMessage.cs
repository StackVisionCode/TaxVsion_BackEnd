using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Domain.Sending;

public enum EmailStream
{
    Transactional,
    Bulk,
}

public enum SentMessageStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    Suppressed,
    ProviderNotConfigured,
}

/// <summary>
/// Representa un intento único e idempotente de enviar un email transaccional o de campaña.
/// Nunca persiste el HTML/text del cuerpo — solo <see cref="RenderedHtmlChecksum"/> para audit;
/// el contenido material vive en Scribe/CloudStorage. Ver Postmaster_Baseline_Audit.md §3.
/// </summary>
public sealed class SentMessage : TenantEntity
{
    private readonly List<SentMessageRecipient> _recipients = [];
    private readonly List<SentMessageEvent> _events = [];
    private readonly List<InlineAsset> _inlineAssets = [];
    private readonly List<OutboundAttachmentRef> _attachments = [];
    private readonly List<string> _references = [];

    private const long MaxTotalInlineAssetsBytes = 5 * 1024 * 1024;

    private SentMessage() { }

    public Guid? NotificationLogId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string FromAddress { get; private set; } = default!;
    public string? FromDisplayName { get; private set; }
    public string? ReplyTo { get; private set; }
    public EmailStream Stream { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public SentMessageStatus Status { get; private set; }
    public DateTime QueuedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? LastEventAtUtc { get; private set; }
    public string? ErrorReason { get; private set; }
    public string? TemplateKey { get; private set; }
    public string? RenderedHtmlChecksum { get; private set; }
    public int MimeSize { get; private set; }
    public string? Metadata { get; private set; }
    public ProviderScope RequiredProviderScope { get; private set; }

    /// <summary>Correlación de vuelta a Correspondence (D3 Compose §11.3) — null para el canal existente de notificaciones automáticas.</summary>
    public Guid? CorrespondenceDraftId { get; private set; }

    /// <summary>Message-ID (RFC 5322) del mensaje al que este envío responde — null si es correspondencia nueva.</summary>
    public string? InReplyToInternetMessageId { get; private set; }

    /// <summary>Message-ID del proveedor una vez enviado (Gmail threadId / Graph conversationId) — se conoce recién tras <see cref="MarkAsSent"/>.</summary>
    public string? ProviderThreadId { get; private set; }

    public IReadOnlyCollection<SentMessageRecipient> Recipients => _recipients.AsReadOnly();
    public IReadOnlyCollection<SentMessageEvent> Events => _events.AsReadOnly();
    public IReadOnlyCollection<InlineAsset> InlineAssets => _inlineAssets.AsReadOnly();
    public IReadOnlyCollection<OutboundAttachmentRef> Attachments => _attachments.AsReadOnly();
    public IReadOnlyCollection<string> References => _references.AsReadOnly();

    public static Result<SentMessage> Queue(
        Guid tenantId,
        string idempotencyKey,
        string subject,
        string fromAddress,
        EmailStream stream,
        string providerCode,
        Guid? notificationLogId,
        string? correlationId,
        string? fromDisplayName,
        string? replyTo,
        string? templateKey,
        DateTime queuedAtUtc,
        ProviderScope requiredProviderScope = ProviderScope.System,
        Guid? correspondenceDraftId = null,
        string? inReplyToInternetMessageId = null,
        IReadOnlyList<string>? references = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SentMessage>(new Error("SentMessage.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            return Result.Failure<SentMessage>(
                new Error("SentMessage.IdempotencyKey", "IdempotencyKey is required and must be at most 200 chars.")
            );

        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure<SentMessage>(new Error("SentMessage.Subject", "Subject is required."));

        var fromValidation = ValidateEmailAddress(fromAddress, "SentMessage.FromAddress");
        if (fromValidation.IsFailure)
            return Result.Failure<SentMessage>(fromValidation.Error);

        string? normalizedReplyTo = null;
        if (replyTo is not null)
        {
            var replyToValidation = ValidateEmailAddress(replyTo, "SentMessage.ReplyTo");
            if (replyToValidation.IsFailure)
                return Result.Failure<SentMessage>(replyToValidation.Error);
            normalizedReplyTo = replyToValidation.Value;
        }

        if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Length > 50)
            return Result.Failure<SentMessage>(
                new Error("SentMessage.ProviderCode", "ProviderCode is required and must be at most 50 chars.")
            );

        var message = new SentMessage
        {
            Id = Guid.NewGuid(),
            NotificationLogId = notificationLogId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            Subject = Truncate(subject, 500),
            FromAddress = fromValidation.Value,
            FromDisplayName = fromDisplayName is { Length: > 100 } ? fromDisplayName[..100] : fromDisplayName,
            ReplyTo = normalizedReplyTo,
            Stream = stream,
            ProviderCode = providerCode,
            Status = SentMessageStatus.Queued,
            QueuedAtUtc = queuedAtUtc,
            TemplateKey = templateKey is { Length: > 200 } ? templateKey[..200] : templateKey,
            RequiredProviderScope = requiredProviderScope,
            CorrespondenceDraftId = correspondenceDraftId,
            InReplyToInternetMessageId = inReplyToInternetMessageId is { Length: > 500 }
                ? inReplyToInternetMessageId[..500]
                : inReplyToInternetMessageId,
        };
        message.SetTenant(tenantId);
        message._references.AddRange(references ?? []);
        message._events.Add(
            SentMessageEvent.Create(tenantId, message.Id, null, SentMessageEventType.Queued, queuedAtUtc, null, null)
        );

        return Result.Success(message);
    }

    /// <summary>Agrega un destinatario. Rechaza duplicados (misma Address+Type dentro del mensaje).</summary>
    public Result<SentMessageRecipient> AddRecipient(string address, RecipientType type, string? displayName)
    {
        var validation = ValidateEmailAddress(address, "SentMessage.Recipient.Address");
        if (validation.IsFailure)
            return Result.Failure<SentMessageRecipient>(validation.Error);

        var normalized = validation.Value;
        if (_recipients.Exists(r => r.Address == normalized && r.Type == type))
            return Result.Failure<SentMessageRecipient>(
                new Error("SentMessage.DuplicateRecipient", $"Recipient {normalized} ({type}) already added.")
            );

        var recipient = SentMessageRecipient.Create(TenantId, Id, normalized, type, displayName);
        _recipients.Add(recipient);
        return Result.Success(recipient);
    }

    /// <summary>Queued → Sending. Sin evento propio (Sending no es un EventType — transición transitoria).</summary>
    public Result MarkAsSending()
    {
        if (Status != SentMessageStatus.Queued)
            return Result.Failure(InvalidTransition(SentMessageStatus.Sending));

        Status = SentMessageStatus.Sending;
        return Result.Success();
    }

    /// <summary>
    /// Sending → Sent. Marca también como Sent a los recipients que seguían Pending — el MTA
    /// aceptó el envelope completo (los bounces individuales llegan después via webhook).
    /// <paramref name="providerThreadId"/> recién se conoce tras el envío (Gmail threadId / Graph
    /// conversationId, D3 Compose §11.3/§13) — null en el canal de notificaciones automáticas, que
    /// nunca continúa un hilo.
    /// </summary>
    public Result MarkAsSent(string? providerMessageId, DateTime sentAtUtc, string? providerThreadId = null)
    {
        if (Status != SentMessageStatus.Sending)
            return Result.Failure(InvalidTransition(SentMessageStatus.Sent));

        Status = SentMessageStatus.Sent;
        SentAtUtc = sentAtUtc;
        LastEventAtUtc = sentAtUtc;
        ErrorReason = null;
        ProviderThreadId = providerThreadId;

        foreach (var recipient in _recipients)
        {
            if (recipient.Status == RecipientStatus.Pending)
                recipient.MarkAsSent(providerMessageId);
        }

        _events.Add(SentMessageEvent.Create(TenantId, Id, null, SentMessageEventType.Sent, sentAtUtc, null, null));
        return Result.Success();
    }

    /// <summary>{Queued, Sending} → Failed. Cubre tanto fallo de resolución de provider como fallo de envío.</summary>
    public Result MarkAsFailed(string reason, DateTime eventAtUtc)
    {
        if (Status is not (SentMessageStatus.Queued or SentMessageStatus.Sending))
            return Result.Failure(InvalidTransition(SentMessageStatus.Failed));

        Status = SentMessageStatus.Failed;
        ErrorReason = Truncate(reason, 500);
        LastEventAtUtc = eventAtUtc;
        _events.Add(
            SentMessageEvent.Create(TenantId, Id, null, SentMessageEventType.Failed, eventAtUtc, null, ErrorReason)
        );
        return Result.Success();
    }

    /// <summary>Queued → Suppressed. El address estaba en la suppression list — nunca se intentó enviar.</summary>
    public Result MarkAsSuppressed(string reason, DateTime eventAtUtc)
    {
        if (Status != SentMessageStatus.Queued)
            return Result.Failure(InvalidTransition(SentMessageStatus.Suppressed));

        Status = SentMessageStatus.Suppressed;
        ErrorReason = Truncate(reason, 500);
        LastEventAtUtc = eventAtUtc;
        _events.Add(
            SentMessageEvent.Create(TenantId, Id, null, SentMessageEventType.Suppressed, eventAtUtc, null, ErrorReason)
        );
        return Result.Success();
    }

    /// <summary>
    /// Queued → ProviderNotConfigured. Tenant scope sin TenantEmailProvider configurado — política
    /// estricta anti-fallback (plan §14.5): nunca reintenta automáticamente.
    /// </summary>
    public Result MarkAsProviderNotConfigured(DateTime eventAtUtc)
    {
        if (Status != SentMessageStatus.Queued)
            return Result.Failure(InvalidTransition(SentMessageStatus.ProviderNotConfigured));

        Status = SentMessageStatus.ProviderNotConfigured;
        LastEventAtUtc = eventAtUtc;
        _events.Add(
            SentMessageEvent.Create(
                TenantId,
                Id,
                null,
                SentMessageEventType.Failed,
                eventAtUtc,
                null,
                "ProviderNotConfigured"
            )
        );
        return Result.Success();
    }

    /// <summary>
    /// Registra un evento a nivel de recipient individual — en la práctica hoy, el único productor real
    /// es la supresión pre-envío (<c>NotificationsEmailSendRequestedConsumer</c>/<c>SendCorrespondenceMessageHandler</c>
    /// solo pasan <see cref="SentMessageEventType.Suppressed"/>). Si trae <paramref name="recipientId"/>,
    /// actualiza el estado de ese recipient vía <see cref="SentMessageRecipient.ApplyEvent"/>.
    /// No hace bubble-up al <see cref="Status"/> del mensaje — eso lo maneja el caller explícitamente
    /// (ver <see cref="MarkAsSuppressed"/>) cuando corresponde. Este método existió para trackear entrega
    /// real por webhook (Delivered/Bounced/Opened/Clicked/Complained); ese tracking se evaluó y se retiró
    /// por no tener ningún productor real — reservado para cuando se reconstruya el tracking de entrega
    /// real, si se decide hacerlo.
    /// </summary>
    public Result RecordDeliveryEvent(
        Guid? recipientId,
        SentMessageEventType eventType,
        DateTime eventAtUtc,
        string? rawPayload,
        string? reason
    )
    {
        if (recipientId is { } id)
        {
            var recipient = _recipients.Find(r => r.Id == id);
            if (recipient is null)
                return Result.Failure(
                    new Error("SentMessage.RecipientNotFound", $"Recipient {id} not found on message {Id}.")
                );
            recipient.ApplyEvent(eventType, reason);
        }

        LastEventAtUtc = eventAtUtc;
        _events.Add(
            SentMessageEvent.Create(
                TenantId,
                Id,
                recipientId,
                eventType,
                eventAtUtc,
                rawPayload,
                reason is { Length: > 500 } ? reason[..500] : reason
            )
        );
        return Result.Success();
    }

    /// <summary>Registra el checksum SHA-256 del HTML renderizado (audit sin persistir el cuerpo).</summary>
    public void RecordRenderedChecksum(string sha256Hex) => RenderedHtmlChecksum = sha256Hex;

    /// <summary>Registra el tamaño final del MIME construido (bytes). Validado ≤5MB por el caller (Fase 3.5).</summary>
    public void RecordMimeSize(int bytes) => MimeSize = bytes;

    /// <summary>Reemplaza el set de imágenes CID a embeber. Rechaza si la suma supera 5MB.</summary>
    public Result RecordInlineAssets(IReadOnlyList<InlineAsset> assets)
    {
        long totalBytes = 0;
        foreach (var asset in assets)
            totalBytes += asset.SizeBytes;

        if (totalBytes > MaxTotalInlineAssetsBytes)
            return Result.Failure(
                new Error(
                    "SentMessage.InlineAssetsTooLarge",
                    $"Total inline assets size {totalBytes} exceeds the {MaxTotalInlineAssetsBytes} bytes limit."
                )
            );

        _inlineAssets.Clear();
        _inlineAssets.AddRange(assets);
        return Result.Success();
    }

    /// <summary>
    /// Reemplaza el set de adjuntos a enviar (D3 Compose §11.3/§12) — a diferencia de
    /// <see cref="RecordInlineAssets"/>, sin cap acá: el límite real depende del proveedor resuelto
    /// recién al momento del envío (Connectors ya lo aplica, ver <c>SendFailureReason.AttachmentTooLarge</c>).
    /// </summary>
    public void RecordAttachments(IReadOnlyList<OutboundAttachmentRef> attachments)
    {
        _attachments.Clear();
        _attachments.AddRange(attachments);
    }

    private static Result<string> ValidateEmailAddress(string address, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Result.Failure<string>(new Error(errorCode, "Email address is required."));

        var trimmed = address.Trim();
        if (trimmed.Length > 320 || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<string>(new Error(errorCode, $"'{address}' is not a valid email address."));

        return Result.Success(trimmed.ToLowerInvariant());
    }

    private Error InvalidTransition(SentMessageStatus target) =>
        new("SentMessage.InvalidTransition", $"Cannot transition SentMessage from {Status} to {target}.");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
