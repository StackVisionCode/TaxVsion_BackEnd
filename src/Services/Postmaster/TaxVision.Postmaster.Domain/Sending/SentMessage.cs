using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.ValueObjects;

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
    private readonly List<DeliveryAttempt> _deliveryAttempts = [];

    private const long MaxTotalInlineAssetsBytes = 5 * 1024 * 1024;

    private SentMessage() { }

    public Guid? NotificationLogId { get; private set; }

    /// <summary>Intento de Notification que pidió este envío; los avisos de entrega posteriores lo devuelven.</summary>
    public Guid? DispatchAttemptId { get; private set; }
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

    /// <summary>Por dónde salió de verdad: proveedor del sistema o buzón conectado del tenant.</summary>
    public DeliveryLane DeliveryLane { get; private set; }

    /// <summary>Correlación de vuelta a Correspondence (D3 Compose §11.3) — null para el canal existente de notificaciones automáticas.</summary>
    public Guid? CorrespondenceDraftId { get; private set; }

    /// <summary>Correlación de vuelta al microservicio de Campaña — null salvo que el envío haya sido originado por una campaña.</summary>
    public Guid? CampaignId { get; private set; }

    /// <summary>Message-ID (RFC 5322) del mensaje al que este envío responde — null si es correspondencia nueva.</summary>
    public string? InReplyToInternetMessageId { get; private set; }

    /// <summary>Message-ID del proveedor una vez enviado (Gmail threadId / Graph conversationId) — se conoce recién tras <see cref="MarkAsSent"/>.</summary>
    public string? ProviderThreadId { get; private set; }

    /// <summary>
    /// Message-ID RFC con el que salió de verdad (sin los signos de menor/mayor). Por SMTP es el nuestro;
    /// por buzón puede ser otro (Graph pone el suyo). Con él se atan los avisos de no entrega.
    /// </summary>
    public string? InternetMessageId { get; private set; }

    public IReadOnlyCollection<SentMessageRecipient> Recipients => _recipients.AsReadOnly();
    public IReadOnlyCollection<SentMessageEvent> Events => _events.AsReadOnly();
    public IReadOnlyCollection<InlineAsset> InlineAssets => _inlineAssets.AsReadOnly();
    public IReadOnlyCollection<OutboundAttachmentRef> Attachments => _attachments.AsReadOnly();
    public IReadOnlyCollection<string> References => _references.AsReadOnly();

    /// <summary>Por dónde intentó salir y cómo le fue. Con fallback hay dos: el buzón que falló y el sistema.</summary>
    public IReadOnlyCollection<DeliveryAttempt> DeliveryAttempts => _deliveryAttempts.AsReadOnly();

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
        IReadOnlyList<string>? references = null,
        Guid? campaignId = null,
        DeliveryLane deliveryLane = DeliveryLane.SystemProvider,
        Guid? dispatchAttemptId = null
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
            DispatchAttemptId = dispatchAttemptId,
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
            DeliveryLane = deliveryLane,
            CorrespondenceDraftId = correspondenceDraftId,
            CampaignId = campaignId,
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
                recipient.MarkAsSent(providerMessageId, sentAtUtc);
        }
        RecordAttempt(succeeded: true, failureReason: null, sentAtUtc);

        _events.Add(SentMessageEvent.Create(TenantId, Id, null, SentMessageEventType.Sent, sentAtUtc, null, null));
        return Result.Success();
    }

    /// <summary>{Queued, Sending} → Failed. Cubre tanto fallo de resolución de provider como fallo de envío.</summary>
    public Result MarkAsFailed(string reason, DateTime eventAtUtc)
    {
        if (Status is not (SentMessageStatus.Queued or SentMessageStatus.Sending))
            return Result.Failure(InvalidTransition(SentMessageStatus.Failed));

        // Desde Queued no hubo intento: falló antes, al resolver por dónde mandarlo.
        if (Status == SentMessageStatus.Sending)
            RecordAttempt(succeeded: false, reason, eventAtUtc);
        Status = SentMessageStatus.Failed;
        ErrorReason = Truncate(reason, 500);
        LastEventAtUtc = eventAtUtc;
        _events.Add(
            SentMessageEvent.Create(TenantId, Id, null, SentMessageEventType.Failed, eventAtUtc, null, ErrorReason)
        );
        return Result.Success();
    }

    /// <summary>
    /// Falló el buzón de la oficina y se reintenta por el proveedor del sistema, en el mismo mensaje
    /// lógico (misma clave de idempotencia). El intento del buzón queda registrado; las respuestas van a
    /// la oficina por Reply-To.
    /// </summary>
    public Result FallBackToSystemProvider(
        string failureReason,
        string systemProviderCode,
        string systemFromAddress,
        string? fromDisplayName,
        string replyTo,
        DateTime nowUtc
    )
    {
        if (Status != SentMessageStatus.Sending || DeliveryLane != DeliveryLane.TenantMailbox)
            return Result.Failure(
                new Error(
                    "SentMessage.FallbackNotAllowed",
                    "Only a message being sent through the office mailbox can fall back to the system provider."
                )
            );

        var from = ValidateEmailAddress(systemFromAddress, "SentMessage.FromAddress");
        if (from.IsFailure)
            return Result.Failure(from.Error);
        var reply = ValidateEmailAddress(replyTo, "SentMessage.ReplyTo");
        if (reply.IsFailure)
            return Result.Failure(reply.Error);
        if (string.IsNullOrWhiteSpace(systemProviderCode) || systemProviderCode.Length > 50)
            return Result.Failure(
                new Error("SentMessage.ProviderCode", "ProviderCode is required and must be at most 50 chars.")
            );

        RecordAttempt(succeeded: false, failureReason, nowUtc);
        DeliveryLane = DeliveryLane.SystemProvider;
        RequiredProviderScope = ProviderScope.System;
        ProviderCode = systemProviderCode;
        FromAddress = from.Value;
        FromDisplayName = fromDisplayName is { Length: > 100 } ? fromDisplayName[..100] : fromDisplayName;
        ReplyTo = reply.Value;
        InternetMessageId = null;
        LastEventAtUtc = nowUtc;
        _events.Add(
            SentMessageEvent.Create(
                TenantId,
                Id,
                null,
                SentMessageEventType.Fallback,
                nowUtc,
                null,
                Truncate(failureReason, 500)
            )
        );
        return Result.Success();
    }

    private void RecordAttempt(bool succeeded, string? failureReason, DateTime attemptedAtUtc) =>
        _deliveryAttempts.Add(
            DeliveryAttempt.Create(TenantId, Id, DeliveryLane, ProviderCode, succeeded, failureReason, attemptedAtUtc)
        );

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

    /// <summary>La dirección estaba en la lista de supresión: nunca se le intenta enviar.</summary>
    public Result MarkRecipientSuppressed(Guid recipientId, string reason, DateTime suppressedAtUtc)
    {
        var recipient = _recipients.Find(r => r.Id == recipientId);
        if (recipient is null)
            return Result.Failure(RecipientNotFound(recipientId));

        recipient.MarkAsSuppressed(reason, suppressedAtUtc);
        AppendRecipientEvent(recipient, SentMessageEventType.Suppressed, suppressedAtUtc, null, reason);
        return Result.Success();
    }

    /// <summary>El relay rechazó esta dirección durante el envío (5xx). Se llama antes de <see cref="MarkAsSent"/>.</summary>
    public Result MarkRecipientRejectedAtSend(Guid recipientId, string reason, DateTime rejectedAtUtc)
    {
        if (Status != SentMessageStatus.Sending)
            return Result.Failure(
                new Error("SentMessage.NotSending", "Recipients can only be rejected while the message is sending.")
            );

        var recipient = _recipients.Find(r => r.Id == recipientId);
        if (recipient is null)
            return Result.Failure(RecipientNotFound(recipientId));

        recipient.MarkRejectedAtSend(reason, rejectedAtUtc);
        AppendRecipientEvent(recipient, SentMessageEventType.Rejected, rejectedAtUtc, null, reason);
        return Result.Success();
    }

    public void RecordInternetMessageId(string internetMessageId)
    {
        var normalized = NormalizeMessageId(internetMessageId);
        if (normalized.Length > 0)
            InternetMessageId = Truncate(normalized, 500);
    }

    public static string NormalizeMessageId(string messageId) => messageId.Trim().Trim('<', '>').Trim();

    /// <summary>Guarda la respuesta del relay en los destinatarios que aceptó.</summary>
    public void RecordProviderAcceptance(string acceptance)
    {
        foreach (var recipient in _recipients)
        {
            if (recipient.Status == RecipientStatus.Sent)
                recipient.RecordProviderAcceptance(acceptance);
        }
    }

    public SentMessageRecipient? FindRecipientByAddress(string address)
    {
        var normalized = address.Trim().ToLowerInvariant();
        return _recipients.Find(r => r.Address == normalized);
    }

    /// <summary>
    /// Aplica un aviso de entrega a un destinatario y lo deja en el historial. Si llegó tarde, el
    /// estado no cambia pero el aviso igual queda registrado. Devuelve true si cambió algo que vale
    /// la pena avisar: un outcome nuevo, o la primera apertura/clic/baja.
    /// </summary>
    public Result<bool> ApplyDeliveryFeedback(Guid recipientId, DeliveryFeedback feedback, string? rawPayload)
    {
        var recipient = _recipients.Find(r => r.Id == recipientId);
        if (recipient is null)
            return Result.Failure<bool>(RecipientNotFound(recipientId));

        var outcomeBefore = recipient.Outcome;
        var firstOfItsKind = IsFirstSignal(recipient, feedback);
        var applied =
            feedback.Kind == DeliveryFeedbackKind.Outcome
                ? ApplyOutcome(recipient, feedback)
                : ApplySignal(recipient, feedback);
        if (applied.IsFailure)
            return Result.Failure<bool>(applied.Error);

        AppendRecipientEvent(
            recipient,
            EventTypeFor(feedback),
            feedback.OccurredAtUtc,
            rawPayload,
            feedback.Diagnostic
        );
        var changed =
            feedback.Kind == DeliveryFeedbackKind.Outcome ? recipient.Outcome != outcomeBefore : firstOfItsKind;
        return Result.Success(changed);
    }

    private static bool IsFirstSignal(SentMessageRecipient recipient, DeliveryFeedback feedback) =>
        feedback.Signal switch
        {
            EngagementSignal.Opened => recipient.OpenCount == 0,
            EngagementSignal.Clicked => recipient.ClickCount == 0,
            EngagementSignal.Unsubscribed => recipient.UnsubscribedAtUtc is null,
            _ => false,
        };

    /// <summary>Lo más relevante para mostrar del mensaje: el peor outcome entre sus destinatarios.</summary>
    public DeliveryOutcome? DeliverySummary
    {
        get
        {
            DeliveryOutcome? summary = null;
            foreach (var recipient in _recipients)
            {
                if (
                    recipient.Outcome is { } outcome
                    && (summary is not { } current || Severity(outcome) > Severity(current))
                )
                    summary = outcome;
            }
            return summary;
        }
    }

    private static Result ApplyOutcome(SentMessageRecipient recipient, DeliveryFeedback feedback) =>
        feedback.Outcome switch
        {
            DeliveryOutcome.Delivered => recipient.RecordDelivered(feedback),
            DeliveryOutcome.Deferred => recipient.RecordDeferred(feedback),
            DeliveryOutcome.SoftBounced => recipient.RecordBounce(feedback, isHard: false),
            DeliveryOutcome.HardBounced => recipient.RecordBounce(feedback, isHard: true),
            DeliveryOutcome.Rejected => recipient.RecordRejected(feedback),
            DeliveryOutcome.Complained => recipient.RecordComplaint(feedback),
            // Accepted solo lo pone nuestro propio envío, nunca un aviso externo.
            _ => Result.Failure(
                new Error("SentMessage.UnsupportedFeedback", $"Outcome '{feedback.Outcome}' cannot come from feedback.")
            ),
        };

    private static Result ApplySignal(SentMessageRecipient recipient, DeliveryFeedback feedback) =>
        feedback.Signal switch
        {
            EngagementSignal.Opened => recipient.RecordOpened(feedback),
            EngagementSignal.Clicked => recipient.RecordClicked(feedback),
            EngagementSignal.Unsubscribed => recipient.RecordUnsubscribed(feedback),
            _ => Result.Failure(
                new Error("SentMessage.UnsupportedFeedback", $"Signal '{feedback.Signal}' is not supported.")
            ),
        };

    private static SentMessageEventType EventTypeFor(DeliveryFeedback feedback) =>
        feedback.Kind == DeliveryFeedbackKind.Signal
            ? feedback.Signal switch
            {
                EngagementSignal.Opened => SentMessageEventType.Opened,
                EngagementSignal.Clicked => SentMessageEventType.Clicked,
                _ => SentMessageEventType.Unsubscribed,
            }
            : feedback.Outcome switch
            {
                DeliveryOutcome.Delivered => SentMessageEventType.Delivered,
                DeliveryOutcome.Deferred => SentMessageEventType.Deferred,
                DeliveryOutcome.Rejected => SentMessageEventType.Rejected,
                DeliveryOutcome.Complained => SentMessageEventType.Complained,
                _ => SentMessageEventType.Bounced,
            };

    /// <summary>Orden para mostrar: lo que más necesita atención primero; entregado es lo menos.</summary>
    private static int Severity(DeliveryOutcome outcome) =>
        outcome switch
        {
            DeliveryOutcome.Complained => 6,
            DeliveryOutcome.HardBounced => 5,
            DeliveryOutcome.Rejected => 4,
            DeliveryOutcome.SoftBounced => 3,
            DeliveryOutcome.Deferred => 2,
            DeliveryOutcome.Accepted => 1,
            _ => 0,
        };

    private void AppendRecipientEvent(
        SentMessageRecipient recipient,
        SentMessageEventType eventType,
        DateTime eventAtUtc,
        string? rawPayload,
        string? reason
    )
    {
        if (LastEventAtUtc is null || eventAtUtc > LastEventAtUtc)
            LastEventAtUtc = eventAtUtc;
        _events.Add(SentMessageEvent.Create(TenantId, Id, recipient.Id, eventType, eventAtUtc, rawPayload, reason));
    }

    private Error RecipientNotFound(Guid recipientId) =>
        new("SentMessage.RecipientNotFound", $"Recipient {recipientId} not found on message {Id}.");

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
