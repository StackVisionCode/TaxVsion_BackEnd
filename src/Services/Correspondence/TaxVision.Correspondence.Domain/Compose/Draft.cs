using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

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
public sealed class Draft : IHasOwner, ITenantOwned
{
    public const int SubjectMaxLength = 1000;
    public const int FailureReasonMaxLength = 500;
    public const int InternetMessageIdMaxLength = 500;

    private readonly List<DraftRecipient> _recipients = [];
    private readonly List<DraftAttachment> _attachments = [];

    private Draft() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Cliente en cuya ficha queda archivado el correo. Null = correo nuevo a alguien que no es
    /// cliente (proveedor, IRS, colega): sale igual, pero no se archiva en ningún cliente y solo lo
    /// ven quienes ven su buzón de envío. Un reply siempre tiene cliente: nace de un correo entrante,
    /// y solo entran correos de clientes.
    /// </summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>
    /// RBAC Fase 4 (RBAC_Hardening_Plan.md) — quién creó este draft, para el chequeo de resource
    /// ownership (<see cref="IHasOwner"/>). <see cref="Guid.Empty"/> para filas que existían antes
    /// de que este campo se capturara — nunca coincide con un userId real del JWT, así que el
    /// ownership check falla-cerrado para esos drafts legacy en vez de asumir un dueño que no se
    /// conoce (ver migración additive que agrega esta columna con ese default).
    /// </summary>
    [UserReference(
        UserReferenceKind.ActiveWork,
        HandledBy = "CorrespondenceUserOffboardedConsumer",
        Note = "Abiertos: al sucesor o se descartan; lo enviado no se toca."
    )]
    public Guid CreatedByUserId { get; private set; }

    /// <summary>Referencia opaca al <c>TenantEmailAccount</c> de Connectors desde la que se enviará — sin FK ni navegación, mismo tratamiento que <see cref="Inbox.IncomingEmail.AccountId"/>.</summary>
    public Guid AccountId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string HtmlBody { get; private set; } = default!;
    public string? TextBody { get; private set; }
    public DraftStatus Status { get; private set; }
    public Guid? SentMessageId { get; private set; }

    /// <summary>Message-ID con el que salió (sin &lt;&gt;). La respuesta del cliente lo cita en In-Reply-To/References.</summary>
    public string? InternetMessageId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? LastAutoSavedAtUtc { get; private set; }

    // Soft-delete del enviado (Sent): null = visible; set = en la papelera.
    public DateTime? DeletedAtUtc { get; private set; }
    public bool IsDeleted => DeletedAtUtc is not null;

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
    /// nueva (no hay thread hasta que el customer responda); se fija una sola vez, en
    /// <see cref="CreateReply"/> o en <see cref="LinkToThread"/> (aviso de no entrega), y nunca vuelve a cambiar.
    /// </summary>
    public Guid? EmailThreadId { get; private set; }

    public IReadOnlyCollection<DraftRecipient> Recipients => _recipients.AsReadOnly();

    /// <summary>Direcciones de un tipo (To/Cc/Bcc) en el orden en que se cargaron.</summary>
    public IReadOnlyList<string> AddressesOf(EmailRecipientType type) =>
        _recipients.Where(r => r.Type == type).Select(r => r.Address).ToList();

    public IReadOnlyCollection<DraftAttachment> Attachments => _attachments.AsReadOnly();

    /// <summary>
    /// RBAC Fase 5 (RBAC_Hardening_Plan.md) — implementación de <see cref="ITenantOwned"/> para el
    /// <c>HasQueryFilter</c> global de <c>CorrespondenceDbContext</c>. <see cref="TenantId"/> ya se
    /// fija una sola vez en <see cref="CreateNew"/>/<see cref="CreateReply"/> — este método existe
    /// solo para satisfacer la interfaz, EF Core nunca lo invoca (materializa filas ya persistidas
    /// con el TenantId original vía el mapeo de columna, no vía este setter).
    /// </summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    /// <summary>
    /// Correspondencia nueva desde cero — subject/body vacíos, el autoguardado los llena a medida que
    /// el usuario escribe. <paramref name="customerId"/> null = no se archiva en ningún cliente.
    /// </summary>
    public static Result<Draft> CreateNew(Guid tenantId, Guid? customerId, Guid accountId, Guid createdByUserId)
    {
        // Guid.Empty no es "sin cliente": es un id mal armado. Sin cliente se pide con null explícito.
        if (customerId == Guid.Empty)
            return Result.Failure<Draft>(
                new Error("Draft.CustomerIdInvalid", "CustomerId must be a valid id or null.")
            );

        var validationError = ValidateIds(tenantId, accountId, createdByUserId);
        if (validationError is not null)
            return Result.Failure<Draft>(validationError);

        var now = DateTime.UtcNow;
        return Result.Success(
            new Draft
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                CreatedByUserId = createdByUserId,
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
        Guid createdByUserId,
        ReplyContext replyContext,
        string originalSubject
    )
    {
        ArgumentNullException.ThrowIfNull(replyContext);

        if (customerId == Guid.Empty)
            return Result.Failure<Draft>(new Error("Draft.CustomerIdRequired", "CustomerId is required."));

        var validationError = ValidateIds(tenantId, accountId, createdByUserId);
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
                CreatedByUserId = createdByUserId,
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
    /// Reenvío tras un aviso de no entrega: un borrador nuevo con el asunto, el cuerpo y los adjuntos del enviado
    /// original (mismos archivos de CloudStorage), dirigido SOLO a <paramref name="recipient"/> (el destinatario
    /// corregido o el mismo). Queda en la misma conversación y conserva el contexto de respuesta, si lo había, para
    /// que el reenvío salga en el mismo hilo. Lo revisa y lo envía el usuario.
    /// </summary>
    public static Result<Draft> CreateResend(
        Draft original,
        EmailAddress recipient,
        Guid createdByUserId,
        Guid emailThreadId
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(recipient);

        if (original.Status != DraftStatus.Sent)
            return Result.Failure<Draft>(new Error("Draft.ResendRequiresSent", "Only a sent email can be resent."));
        if (emailThreadId == Guid.Empty)
            return Result.Failure<Draft>(new Error("Draft.EmailThreadIdRequired", "EmailThreadId is required."));

        var validationError = ValidateIds(original.TenantId, original.AccountId, createdByUserId);
        if (validationError is not null)
            return Result.Failure<Draft>(validationError);

        var now = DateTime.UtcNow;
        var draft = new Draft
        {
            Id = Guid.NewGuid(),
            TenantId = original.TenantId,
            CustomerId = original.CustomerId,
            CreatedByUserId = createdByUserId,
            AccountId = original.AccountId,
            Subject = original.Subject,
            HtmlBody = original.HtmlBody,
            TextBody = original.TextBody,
            Status = DraftStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ReplyContext = original.ReplyContext,
            EmailThreadId = emailThreadId,
        };

        // Mismo nombre visible si el destinatario no cambió.
        var displayName = original
            ._recipients.FirstOrDefault(r =>
                string.Equals(r.Address, recipient.NormalizedValue, StringComparison.OrdinalIgnoreCase)
            )
            ?.DisplayName;
        draft._recipients.Add(
            DraftRecipient.Create(draft.TenantId, draft.Id, recipient, EmailRecipientType.To, displayName)
        );
        foreach (var attachment in original._attachments)
            draft._attachments.Add(DraftAttachment.Create(draft.TenantId, draft.Id, attachment.ToRef()));

        return Result.Success(draft);
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

        _attachments.Add(DraftAttachment.Create(TenantId, Id, attachmentRef));
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

    /// <summary>
    /// Reasigna el autor de un borrador ABIERTO al sucesor cuando el autor original se retira del
    /// tenant (offboard). Solo válido en <see cref="DraftStatus.Draft"/> — lo enviado es historial
    /// inmutable y su autoría no se reescribe.
    /// </summary>
    public Result ReassignAuthor(Guid newUserId)
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(ReassignAuthor)));

        if (newUserId == Guid.Empty)
            return Result.Failure(new Error("Draft.CreatedByUserIdRequired", "CreatedByUserId is required."));

        CreatedByUserId = newUserId;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Cambia el buzón desde el que saldrá un borrador ABIERTO (al retirar a su autor, si salía de un buzón
    /// personal suyo que Connectors desconecta y purga). Lo enviado no se toca.
    /// </summary>
    public Result MoveToAccount(Guid accountId)
    {
        if (Status != DraftStatus.Draft)
            return Result.Failure(InvalidTransition(nameof(MoveToAccount)));

        if (accountId == Guid.Empty)
            return Result.Failure(new Error("Draft.AccountIdRequired", "AccountId is required."));

        if (AccountId == accountId)
            return Result.Success();

        AccountId = accountId;
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
    public Result MarkSent(Guid sentMessageId, string? internetMessageId = null)
    {
        if (Status != DraftStatus.Sending)
            return Result.Failure(InvalidTransition(nameof(MarkSent)));

        if (sentMessageId == Guid.Empty)
            return Result.Failure(new Error("Draft.SentMessageIdRequired", "SentMessageId is required."));

        Status = DraftStatus.Sent;
        SentMessageId = sentMessageId;
        InternetMessageId = NormalizeMessageId(internetMessageId);
        FailureReason = null;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Message-ID comparable: sin espacios ni &lt;&gt;; null si viene vacío o no cabe (truncarlo lo inutiliza).</summary>
    public static string? NormalizeMessageId(string? raw)
    {
        var trimmed = raw?.Trim().Trim('<', '>').Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > InternetMessageIdMaxLength ? null : trimmed;
    }

    // Papelera del enviado. Solo un Sent es un "mensaje" borrable. Idempotente.
    public Result SoftDelete(DateTime nowUtc)
    {
        if (Status != DraftStatus.Sent)
            return Result.Failure(InvalidTransition(nameof(SoftDelete)));

        DeletedAtUtc ??= nowUtc;
        return Result.Success();
    }

    public void Restore()
    {
        DeletedAtUtc = null;
    }

    /// <summary>
    /// Resultado de entrega de un destinatario de este correo ya enviado (Postmaster lo correlaciona). Devuelve
    /// <c>true</c> si cambió el estado; <c>false</c> si es repetido o más viejo que el último aplicado.
    /// </summary>
    public Result<bool> RecordDeliveryOutcome(
        string recipientAddress,
        DeliveryOutcome outcome,
        string? category,
        DateTime occurredAtUtc
    )
    {
        if (Status != DraftStatus.Sent)
            return Result.Failure<bool>(InvalidTransition(nameof(RecordDeliveryOutcome)));

        var recipient = _recipients.Find(r =>
            string.Equals(r.Address, recipientAddress?.Trim(), StringComparison.OrdinalIgnoreCase)
        );
        if (recipient is null)
            return Result.Failure<bool>(
                new Error("Draft.RecipientNotFound", "The recipient is not part of this email.")
            );

        return Result.Success(recipient.RecordDelivery(outcome, category, occurredAtUtc));
    }

    /// <summary>
    /// Un correo nuevo enviado no tiene conversación hasta que algo llega sobre él (una respuesta o un aviso de no
    /// entrega): ahí se le crea una y se enlaza acá. Solo una vez; enlazar al mismo hilo es un no-op.
    /// </summary>
    public Result LinkToThread(Guid emailThreadId)
    {
        if (Status != DraftStatus.Sent)
            return Result.Failure(InvalidTransition(nameof(LinkToThread)));
        if (emailThreadId == Guid.Empty)
            return Result.Failure(new Error("Draft.EmailThreadIdRequired", "EmailThreadId is required."));
        if (EmailThreadId == emailThreadId)
            return Result.Success();
        if (EmailThreadId is not null)
            return Result.Failure(
                new Error("Draft.AlreadyInThread", "This email already belongs to another conversation.")
            );

        EmailThreadId = emailThreadId;
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

    private static Error? ValidateIds(Guid tenantId, Guid accountId, Guid createdByUserId)
    {
        if (tenantId == Guid.Empty)
            return new Error("Draft.TenantIdRequired", "TenantId is required.");
        if (accountId == Guid.Empty)
            return new Error("Draft.AccountIdRequired", "AccountId is required.");
        if (createdByUserId == Guid.Empty)
            return new Error("Draft.CreatedByUserIdRequired", "CreatedByUserId is required.");

        return null;
    }

    private Error InvalidTransition(string operation) =>
        new("Draft.InvalidTransition", $"Cannot perform '{operation}' while Status is {Status}.");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
