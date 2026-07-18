using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Un correo en redacción — nuevo o reply. Dueña exclusiva de Correspondence: Postmaster no
/// conoce el concepto de "borrador", solo recibe un mensaje ya armado y listo para enviar vía
/// <c>POST /postmaster/correspondence-messages</c> (plan §0/§14).
///
/// <para>
/// <see cref="Status"/> modela el resultado de UNA llamada HTTP síncrona y bloqueante a
/// Postmaster, no un workflow asíncrono (ver <see cref="DraftStatus"/>) — esto es la razón de
/// que las transiciones sean tan estrictas. Una vez que <see cref="MarkSending"/> se llamó, el
/// usuario ya apretó "Enviar" y esa request HTTP está en curso: no hay forma de cancelarla a
/// mitad de camino, por eso <see cref="Discard"/>/<see cref="AutoSave"/>/<see cref="AttachFile"/>/
/// <see cref="RemoveAttachment"/> solo son válidos desde <see cref="DraftStatus.Draft"/>. Y una
/// vez <see cref="DraftStatus.Sent"/>, el mensaje ya salió por el proveedor real — no existe
/// "un-enviar" un correo, así que tampoco se puede volver a <see cref="Discard"/> desde ahí.
/// </para>
/// </summary>
public sealed class Draft
{
    public const int SubjectMaxLength = 1000;
    public const int FailureReasonMaxLength = 500;

    private readonly List<DraftRecipient> _recipients = [];
    private readonly List<DraftAttachmentRef> _attachments = [];

    private Draft() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }

    /// <summary>Referencia opaca al <c>TenantEmailAccount</c> de Connectors desde la que se enviará — sin FK ni navegación, mismo tratamiento que <see cref="Inbox.IncomingEmail.AccountId"/>.</summary>
    public Guid AccountId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string HtmlBody { get; private set; } = default!;
    public string? TextBody { get; private set; }
    public DraftStatus Status { get; private set; }
    public Guid? SentMessageId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? LastAutoSavedAtUtc { get; private set; }

    /// <summary>Null para correspondencia nueva, congelado una sola vez para un reply (ver <see cref="ReplyContext"/>).</summary>
    public ReplyContext? ReplyContext { get; private set; }

    /// <summary>
    /// Copia denormalizada de <see cref="Compose.ReplyContext.EmailThreadId"/>, en una columna real
    /// e indexada — no solo dentro del JSON de <see cref="ReplyContext"/> (Fase 15,
    /// <c>ListThreadMessagesHandler</c>, "thread unificado" inbound+outbound). Decisión explícita
    /// (WHY): <c>DraftRepository.FindOpenReplyDraftAsync</c> (Fase 10, Infrastructure) acepta
    /// filtrar <c>ReplyContext</c> en memoria porque el conjunto que acota primero — drafts abiertos
    /// (<see cref="DraftStatus.Draft"/>) de un customer — nunca crece sin límite: un draft sale de
    /// ahí en cuanto se envía o se descarta. Acá el conjunto equivalente sería "TODOS los
    /// <see cref="DraftStatus.Sent"/> de un customer" (no hay salida de ese estado), que sí crece
    /// sin límite con el tiempo — el mismo trade-off de Fase 10 aplicado acá degradaría con el uso
    /// real del sistema en vez de quedarse chico. Por eso, a diferencia de Fase 10, se materializa
    /// el join en esta columna en vez de filtrar el JSON en memoria. Null para correspondencia
    /// nueva (no hay thread hasta que el customer responda); se fija una sola vez en
    /// <see cref="CreateReply"/>, nunca vuelve a cambiar.
    /// </summary>
    public Guid? EmailThreadId { get; private set; }

    public IReadOnlyCollection<DraftRecipient> Recipients => _recipients.AsReadOnly();
    public IReadOnlyCollection<DraftAttachmentRef> Attachments => _attachments.AsReadOnly();

    /// <summary>Correspondencia nueva desde cero — subject/body vacíos, el autoguardado los llena a medida que el usuario escribe.</summary>
    public static Result<Draft> CreateNew(Guid tenantId, Guid customerId, Guid accountId)
    {
        var validationError = ValidateIds(tenantId, customerId, accountId);
        if (validationError is not null)
            return Result.Failure<Draft>(validationError);

        var now = DateTime.UtcNow;
        return Result.Success(
            new Draft
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                AccountId = accountId,
                Subject = string.Empty,
                HtmlBody = string.Empty,
                TextBody = null,
                Status = DraftStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ReplyContext = null,
                EmailThreadId = null,
            }
        );
    }

    /// <summary>Un reply — <paramref name="originalSubject"/> es el subject del <see cref="Inbox.IncomingEmail"/> original, del que se deriva el subject del reply con el prefijo "Re: ".</summary>
    public static Result<Draft> CreateReply(
        Guid tenantId,
        Guid customerId,
        Guid accountId,
        ReplyContext replyContext,
        string originalSubject
    )
    {
        ArgumentNullException.ThrowIfNull(replyContext);

        var validationError = ValidateIds(tenantId, customerId, accountId);
        if (validationError is not null)
            return Result.Failure<Draft>(validationError);

        if (string.IsNullOrWhiteSpace(originalSubject))
            return Result.Failure<Draft>(new Error("Draft.OriginalSubjectRequired", "OriginalSubject is required."));

        var now = DateTime.UtcNow;
        return Result.Success(
            new Draft
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                AccountId = accountId,
                Subject = Truncate(BuildReplySubject(originalSubject), SubjectMaxLength),
                HtmlBody = string.Empty,
                TextBody = null,
                Status = DraftStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ReplyContext = replyContext,
                EmailThreadId = replyContext.EmailThreadId,
            }
        );
    }

    /// <summary>
    /// Autoguardado parcial (semántica PATCH) — solo pisa los campos no-null. Pensado para ser
    /// llamado cada pocos segundos mientras el usuario escribe (debounced desde el frontend,
    /// Fase 11); por eso no genera domain event de integración, solo mueve
    /// <see cref="UpdatedAtUtc"/>/<see cref="LastAutoSavedAtUtc"/>.
    /// </summary>
    public Result AutoSave(
        string? subject,
        string? htmlBody,
        string? textBody,
        IReadOnlyList<DraftRecipientData>? recipients
    )
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(AutoSave)));

        if (subject is not null)
        {
            if (subject.Length > SubjectMaxLength)
                return Result.Failure(
                    new Error("Draft.SubjectTooLong", $"Subject must not exceed {SubjectMaxLength} characters.")
                );
            Subject = subject;
        }

        if (htmlBody is not null)
            HtmlBody = htmlBody;

        if (textBody is not null)
            TextBody = textBody;

        if (recipients is not null)
            ReplaceRecipients(recipients);

        var now = DateTime.UtcNow;
        UpdatedAtUtc = now;
        LastAutoSavedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Adjunta una referencia a un archivo ya subido. Adjuntar el mismo <see cref="DraftAttachmentRef.FileId"/> dos veces es un no-op, no un error.</summary>
    public Result AttachFile(DraftAttachmentRef attachmentRef)
    {
        ArgumentNullException.ThrowIfNull(attachmentRef);

        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(AttachFile)));

        if (IndexOfAttachment(attachmentRef.FileId) >= 0)
            return Result.Success();

        _attachments.Add(attachmentRef);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Idempotente/tolerante: quitar un adjunto que ya no está no es un error, mismo criterio que <see cref="Inbox.EmailThread.Archive"/>.</summary>
    public Result RemoveAttachment(Guid fileId)
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(RemoveAttachment)));

        var index = IndexOfAttachment(fileId);
        if (index < 0)
            return Result.Success();

        _attachments.RemoveAt(index);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Draft → Discarded. Un mensaje ya enviado no se puede "descartar" — ver nota de clase sobre por qué esto solo vale desde <see cref="DraftStatus.Draft"/>.</summary>
    public Result Discard()
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(Discard)));

        Status = DraftStatus.Discarded;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Draft → Sending. Arranca justo antes de la llamada HTTP síncrona a Postmaster (Fase 14) — el handler, no el aggregate, hace esa llamada.</summary>
    public Result MarkSending()
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(MarkSending)));

        Status = DraftStatus.Sending;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Sending → Sent. Solo alcanzable tras un <see cref="MarkSending"/> previo — nunca directo
    /// desde Draft. WHY no hay un <c>SentAtUtc</c> dedicado: <see cref="UpdatedAtUtc"/> ya captura
    /// el instante exacto de este envío, y ningún otro método de este aggregate vuelve a tocar
    /// <see cref="UpdatedAtUtc"/> una vez <see cref="DraftStatus.Sent"/> (todas las demás
    /// transiciones — <see cref="AutoSave"/>/<see cref="AttachFile"/>/<see cref="RemoveAttachment"/>/
    /// <see cref="Discard"/> — están guardadas a <see cref="DraftStatus.Draft"/> solamente). Un
    /// campo nuevo sería una copia exacta del mismo valor sin ninguna ambigüedad que resolver, así
    /// que Fase 15 (<c>ListThreadMessagesHandler</c>, vista de "mensaje enviado" del thread) lee
    /// <see cref="UpdatedAtUtc"/> directo en vez de agregar esa columna redundante.
    /// </summary>
    public Result MarkSent(Guid sentMessageId)
    {
        if (Status != DraftStatus.Sending)
            return Result.Failure(InvalidTransition(nameof(MarkSent)));

        if (sentMessageId == Guid.Empty)
            return Result.Failure(new Error("Draft.SentMessageIdRequired", "SentMessageId is required."));

        Status = DraftStatus.Sent;
        SentMessageId = sentMessageId;
        FailureReason = null;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Sending → Failed. El usuario puede reintentar creando un nuevo intento de envío sobre el mismo Draft en Fase 14 — este aggregate solo registra el motivo.</summary>
    public Result MarkFailed(string reason)
    {
        if (Status != DraftStatus.Sending)
            return Result.Failure(InvalidTransition(nameof(MarkFailed)));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Draft.FailureReasonRequired", "FailureReason is required."));

        Status = DraftStatus.Failed;
        FailureReason = Truncate(reason, FailureReasonMaxLength);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    private void ReplaceRecipients(IReadOnlyList<DraftRecipientData> recipients)
    {
        _recipients.Clear();
        foreach (var data in recipients)
            _recipients.Add(DraftRecipient.Create(TenantId, Id, data.Address, data.Type, data.DisplayName));
    }

    private int IndexOfAttachment(Guid fileId)
    {
        for (var i = 0; i < _attachments.Count; i++)
        {
            if (_attachments[i].FileId == fileId)
                return i;
        }

        return -1;
    }

    private static string BuildReplySubject(string originalSubject)
    {
        var trimmed = originalSubject.Trim();
        return trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? trimmed : "Re: " + trimmed;
    }

    private static Error? ValidateIds(Guid tenantId, Guid customerId, Guid accountId)
    {
        if (tenantId == Guid.Empty)
            return new Error("Draft.TenantIdRequired", "TenantId is required.");
        if (customerId == Guid.Empty)
            return new Error("Draft.CustomerIdRequired", "CustomerId is required.");
        if (accountId == Guid.Empty)
            return new Error("Draft.AccountIdRequired", "AccountId is required.");

        return null;
    }

    private Error InvalidTransition(string operation) =>
        new("Draft.InvalidTransition", $"Cannot perform '{operation}' while Status is {Status}.");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
