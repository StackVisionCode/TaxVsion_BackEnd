using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Conversación entre el tenant y un destinatario: agrupa los <see cref="IncomingEmail"/>, los enviados
/// (<c>Draft.EmailThreadId</c>) y los avisos de entrega. <see cref="CustomerId"/> null = "Not a client": nació de un
/// aviso de no entrega (D-1) o de un correo de quien todavía no es cliente (D-COR-3). Su visibilidad la decide solo
/// el buzón.
/// </summary>
public sealed class EmailThread : AggregateRoot
{
    public const int SubjectMaxLength = 1000;
    public const int ProviderThreadIdMaxLength = 200;

    /// <summary>Asunto de la conversación cuando el correo original salió sin asunto.</summary>
    public const string NoSubject = "(no subject)";

    private EmailThread() { }

    /// <summary>Null = la otra parte no es cliente.</summary>
    public Guid? CustomerId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string? ProviderThreadId { get; private set; }
    public DateTime FirstMessageAtUtc { get; private set; }
    public DateTime LastMessageAtUtc { get; private set; }
    public int MessageCount { get; private set; }
    public EmailThreadStatus Status { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<EmailThread> NewFromMessage(
        Guid tenantId,
        Guid? customerId,
        string subject,
        string? providerThreadId,
        DateTime firstMessageAtUtc
    )
    {
        var validationError = Validate(tenantId, customerId, subject, providerThreadId);
        if (validationError is not null)
            return Result.Failure<EmailThread>(validationError);

        var thread = new EmailThread
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Subject = subject,
            ProviderThreadId = providerThreadId,
            FirstMessageAtUtc = firstMessageAtUtc,
            LastMessageAtUtc = firstMessageAtUtc,
            MessageCount = 1,
            Status = EmailThreadStatus.Active,
            ArchivedAtUtc = null,
        };
        thread.SetTenant(tenantId);
        return Result.Success(thread);
    }

    /// <summary>
    /// Conversación para un correo nuevo enviado que aún no tenía una (D-1): nace cuando llega su primer aviso de
    /// entrega. <see cref="MessageCount"/> cuenta solo entrantes, así que empieza en 0; el enviado enlazado la
    /// mantiene visible. <paramref name="customerId"/> null = el destinatario no es cliente.
    /// <paramref name="threadId"/> lo elige el caller (el id del enviado): dos avisos simultáneos del mismo correo
    /// chocan en la clave primaria en vez de crear dos conversaciones.
    /// </summary>
    public static Result<EmailThread> NewForSentEmail(
        Guid threadId,
        Guid tenantId,
        Guid? customerId,
        string subject,
        DateTime sentAtUtc
    )
    {
        if (threadId == Guid.Empty)
            return Result.Failure<EmailThread>(new Error("EmailThread.IdRequired", "Id is required."));
        if (customerId == Guid.Empty)
            return Result.Failure<EmailThread>(
                new Error("EmailThread.CustomerIdInvalid", "CustomerId must be a valid id or null.")
            );

        var normalizedSubject = string.IsNullOrWhiteSpace(subject) ? NoSubject : subject.Trim();
        if (normalizedSubject.Length > SubjectMaxLength)
            normalizedSubject = normalizedSubject[..SubjectMaxLength];
        if (tenantId == Guid.Empty)
            return Result.Failure<EmailThread>(new Error("EmailThread.TenantIdRequired", "TenantId is required."));

        var thread = new EmailThread
        {
            Id = threadId,
            CustomerId = customerId,
            Subject = normalizedSubject,
            ProviderThreadId = null,
            FirstMessageAtUtc = sentAtUtc,
            LastMessageAtUtc = sentAtUtc,
            MessageCount = 0,
            Status = EmailThreadStatus.Active,
            ArchivedAtUtc = null,
        };
        thread.SetTenant(tenantId);
        return Result.Success(thread);
    }

    /// <summary>
    /// Llegó un aviso de entrega. Si exige acción (no entregado) la conversación vuelve al Inbox (D-3) y sube
    /// arriba; un Delayed no la mueve (D-4). Devuelve <c>true</c> si cambió el hilo.
    /// </summary>
    public bool RegisterDeliveryNotice(DateTime noticeAtUtc, bool requiresAction)
    {
        if (!requiresAction)
            return false;

        var changed = Status == EmailThreadStatus.Archived;
        Reopen();
        if (noticeAtUtc > LastMessageAtUtc)
        {
            LastMessageAtUtc = noticeAtUtc;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Registra que llegó un mensaje nuevo al hilo. Un hilo archivado vuelve al Inbox, como con un aviso de
    /// no entrega: la respuesta del cliente nunca se descarta. Devuelve <c>true</c> si el hilo se reabrió.
    /// </summary>
    public Result<bool> AppendMessage(DateTime receivedAtUtc)
    {
        var reopened = Status == EmailThreadStatus.Archived;
        Reopen();
        // Un correo viejo (re-sincronización) cuenta, pero no mueve el hilo hacia atrás.
        if (receivedAtUtc > LastMessageAtUtc)
            LastMessageAtUtc = receivedAtUtc;
        MessageCount++;
        return Result.Success(reopened);
    }

    /// <summary>
    /// Completa el id de conversación del proveedor de un hilo que no lo tenía (p. ej. el de un correo nuevo enviado
    /// desde el CRM, cuando llega la primera respuesta). Nunca pisa uno existente. Devuelve <c>true</c> si cambió.
    /// </summary>
    public bool AdoptProviderThreadId(string? providerThreadId)
    {
        if (ProviderThreadId is not null || string.IsNullOrWhiteSpace(providerThreadId))
            return false;
        if (providerThreadId.Length > ProviderThreadIdMaxLength)
            return false;

        ProviderThreadId = providerThreadId;
        return true;
    }

    /// <summary>Quien escribió ya es cliente (COR-05): el hilo pasa a su ficha. No pisa un cliente ya asignado.</summary>
    public bool LinkToCustomer(Guid customerId)
    {
        if (CustomerId is not null || customerId == Guid.Empty)
            return false;

        CustomerId = customerId;
        return true;
    }

    /// <summary>Reposición (COR-02): recalcula contador y fechas tras mover correos entre hilos.</summary>
    public void ResetMessageStats(int messageCount, DateTime firstMessageAtUtc, DateTime lastMessageAtUtc)
    {
        MessageCount = Math.Max(0, messageCount);
        FirstMessageAtUtc = firstMessageAtUtc;
        LastMessageAtUtc = lastMessageAtUtc;
    }

    // Un entrante fue/volvió de la papelera. MessageCount cuenta solo entrantes.
    public void DecrementMessageCount()
    {
        if (MessageCount > 0)
            MessageCount--;
    }

    public void IncrementMessageCount()
    {
        MessageCount++;
    }

    /// <summary>
    /// Idempotente: archivar un hilo ya archivado no pisa el <see cref="ArchivedAtUtc"/> original.
    /// El reloj lo posee el aggregate (no un parámetro del caller) — mismo patrón que
    /// <see cref="Projections.CustomerEmailAddress"/> usa en <c>SoftDelete</c>/<c>Reactivate</c>
    /// dentro de este mismo servicio.
    /// </summary>
    public void Archive(Guid? actorUserId = null, string correlationId = "")
    {
        if (Status != EmailThreadStatus.Archived)
        {
            Status = EmailThreadStatus.Archived;
            ArchivedAtUtc = DateTime.UtcNow;
        }

        // Aunque ya estuviera archivado: archivar también marca leído y eso cambia lo que se ve.
        NotifyStateChanged(actorUserId, correlationId);
    }

    // Archived → Active. Idempotente.
    public void Unarchive(Guid? actorUserId = null, string correlationId = "")
    {
        Reopen();
        NotifyStateChanged(actorUserId, correlationId);
    }

    /// <summary>
    /// Cambió lo que se ve del hilo (archivado, no leídos, mensajes). Los que entran por el sistema lo llaman aparte:
    /// una réplica (CON-07) no avisa.
    /// </summary>
    public void NotifyStateChanged(Guid? actorUserId = null, string correlationId = "") =>
        AddDomainEvent(new EmailThreadStateChangedDomainEvent(TenantId, Id, actorUserId, correlationId));

    private void Reopen()
    {
        if (Status != EmailThreadStatus.Archived)
            return;

        Status = EmailThreadStatus.Active;
        ArchivedAtUtc = null;
    }

    private static Error? Validate(Guid tenantId, Guid? customerId, string subject, string? providerThreadId)
    {
        if (tenantId == Guid.Empty)
            return new Error("EmailThread.TenantIdRequired", "TenantId is required.");
        if (customerId == Guid.Empty)
            return new Error("EmailThread.CustomerIdRequired", "CustomerId is required.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectMaxLength)
            return new Error("EmailThread.SubjectInvalid", "Subject is required and must not exceed 1000 characters.");
        if (providerThreadId is { Length: > ProviderThreadIdMaxLength })
            return new Error("EmailThread.ProviderThreadIdTooLong", "ProviderThreadId must not exceed 200 characters.");

        return null;
    }
}
