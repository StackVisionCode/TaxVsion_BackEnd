using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Delivery;

/// <summary>
/// Aviso de no entrega (o demora) de un correo enviado desde el CRM, mostrado dentro de su conversación.
/// Aggregate propio que referencia el hilo y el borrador por id, igual que <see cref="Inbox.IncomingEmail"/>:
/// la conversación no carga sus avisos para cambiar de estado, y cada aviso tiene su propio leído.
/// Solo nace de un evento ya correlacionado por Postmaster (nunca del filtro de remitentes del Inbox).
/// </summary>
public sealed class DeliveryNotice : ITenantOwned
{
    public const int RecipientMaxLength = 320;
    public const int CategoryMaxLength = 32;
    public const int StatusCodeMaxLength = 16;
    public const int DiagnosticMaxLength = 1000;
    public const int ReportProviderMessageIdMaxLength = 500;

    private DeliveryNotice() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmailThreadId { get; private set; }
    public Guid DraftId { get; private set; }

    /// <summary>Envío de Postmaster: con destinatario y resultado forma la clave de idempotencia.</summary>
    public Guid SentMessageId { get; private set; }

    /// <summary>Cliente del correo original; null = el destinatario no es cliente.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Buzón desde el que salió el correo original (gate de buzón).</summary>
    public Guid AccountId { get; private set; }

    public string Recipient { get; private set; } = default!;
    public DeliveryOutcome Outcome { get; private set; }

    /// <summary>Categoría estable del motivo (InvalidAddress, MailboxFull…); null si Postmaster no la dio.</summary>
    public string? Category { get; private set; }
    public string? StatusCode { get; private set; }

    /// <summary>Respuesta técnica del servidor (soporte). La UI muestra el motivo de la categoría.</summary>
    public string? Diagnostic { get; private set; }

    /// <summary>La dirección quedó suprimida por este resultado (los próximos envíos se bloquean).</summary>
    public bool Suppressed { get; private set; }

    /// <summary>Buzón y id del aviso original en el proveedor; null si llegó por webhook (no hay aviso que mostrar).</summary>
    public Guid? ReportAccountId { get; private set; }
    public string? ReportProviderMessageId { get; private set; }

    /// <summary>Cuándo ocurrió según el proveedor.</summary>
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>Leído compartido por la oficina. Un Delayed nace leído (D-4: no hay nada que hacer todavía).</summary>
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>Un NotDelivered exige acción: cuenta como no leído, sube la conversación y la saca del archivo.</summary>
    public bool RequiresAction => Outcome == DeliveryOutcome.NotDelivered;

    public bool CanViewOriginal => ReportAccountId is not null && !string.IsNullOrEmpty(ReportProviderMessageId);

    /// <summary>Código estable para la UI (texto y acciones de la tarjeta).</summary>
    public string ReasonCode => DeliveryReasonCodes.From(Outcome, Category);

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static Result<DeliveryNotice> Create(
        Guid tenantId,
        Guid emailThreadId,
        Guid draftId,
        Guid sentMessageId,
        Guid? customerId,
        Guid accountId,
        string recipient,
        DeliveryOutcome outcome,
        string? category,
        string? statusCode,
        string? diagnostic,
        bool suppressed,
        Guid? reportAccountId,
        string? reportProviderMessageId,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc
    )
    {
        if (tenantId == Guid.Empty || emailThreadId == Guid.Empty || draftId == Guid.Empty || accountId == Guid.Empty)
            return Result.Failure<DeliveryNotice>(
                new Error("DeliveryNotice.IdsRequired", "TenantId, EmailThreadId, DraftId and AccountId are required.")
            );
        if (sentMessageId == Guid.Empty)
            return Result.Failure<DeliveryNotice>(
                new Error("DeliveryNotice.SentMessageIdRequired", "SentMessageId is required.")
            );
        if (customerId == Guid.Empty)
            return Result.Failure<DeliveryNotice>(
                new Error("DeliveryNotice.CustomerIdInvalid", "CustomerId must be a valid id or null.")
            );
        if (outcome == DeliveryOutcome.Delivered)
            return Result.Failure<DeliveryNotice>(
                new Error("DeliveryNotice.DeliveredHasNoNotice", "A delivered email does not produce a notice.")
            );
        if (string.IsNullOrWhiteSpace(recipient) || recipient.Trim().Length > RecipientMaxLength)
            return Result.Failure<DeliveryNotice>(
                new Error(
                    "DeliveryNotice.RecipientInvalid",
                    "Recipient is required and must not exceed 320 characters."
                )
            );

        // Sin buzón o con un id que no entra en la columna no se puede volver a pedir el aviso: "ver original" queda apagado.
        var canFetch =
            reportAccountId is { } account
            && account != Guid.Empty
            && !string.IsNullOrWhiteSpace(reportProviderMessageId)
            && reportProviderMessageId.Length <= ReportProviderMessageIdMaxLength;

        return Result.Success(
            new DeliveryNotice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmailThreadId = emailThreadId,
                DraftId = draftId,
                SentMessageId = sentMessageId,
                CustomerId = customerId,
                AccountId = accountId,
                Recipient = recipient.Trim().ToLowerInvariant(),
                Outcome = outcome,
                Category = Truncate(category, CategoryMaxLength),
                StatusCode = Truncate(statusCode, StatusCodeMaxLength),
                Diagnostic = Truncate(diagnostic, DiagnosticMaxLength),
                Suppressed = suppressed,
                ReportAccountId = canFetch ? reportAccountId : null,
                ReportProviderMessageId = canFetch ? reportProviderMessageId : null,
                OccurredAtUtc = occurredAtUtc,
                ReceivedAtUtc = receivedAtUtc,
                IsRead = outcome != DeliveryOutcome.NotDelivered,
                ReadAtUtc = outcome != DeliveryOutcome.NotDelivered ? receivedAtUtc : null,
            }
        );
    }

    /// <summary>Idempotente. Devuelve <c>true</c> solo si cambió.</summary>
    public bool MarkRead(DateTime nowUtc)
    {
        if (IsRead)
            return false;
        IsRead = true;
        ReadAtUtc = nowUtc;
        return true;
    }

    /// <summary>Solo un NotDelivered puede volver a no leído: un Delayed nunca cuenta (D-4).</summary>
    public bool MarkUnread()
    {
        if (!IsRead || !RequiresAction)
            return false;
        IsRead = false;
        ReadAtUtc = null;
        return true;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
