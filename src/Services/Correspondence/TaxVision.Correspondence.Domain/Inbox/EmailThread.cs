using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Agrupador de <see cref="IncomingEmail"/> de la misma conversación entre el tenant y un
/// customer. <see cref="CustomerId"/> es obligatorio por el mismo motivo que en
/// <see cref="IncomingEmail"/>: un hilo sin customer no tiene sentido en este modelo.
/// </summary>
public sealed class EmailThread
{
    public const int SubjectMaxLength = 1000;
    public const int ProviderThreadIdMaxLength = 200;

    private EmailThread() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string? ProviderThreadId { get; private set; }
    public DateTime FirstMessageAtUtc { get; private set; }
    public DateTime LastMessageAtUtc { get; private set; }
    public int MessageCount { get; private set; }
    public EmailThreadStatus Status { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }

    public static Result<EmailThread> NewFromMessage(
        Guid tenantId,
        Guid customerId,
        string subject,
        string? providerThreadId,
        DateTime firstMessageAtUtc
    )
    {
        var validationError = Validate(tenantId, customerId, subject, providerThreadId);
        if (validationError is not null)
            return Result.Failure<EmailThread>(validationError);

        return Result.Success(
            new EmailThread
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                Subject = subject,
                ProviderThreadId = providerThreadId,
                FirstMessageAtUtc = firstMessageAtUtc,
                LastMessageAtUtc = firstMessageAtUtc,
                MessageCount = 1,
                Status = EmailThreadStatus.Active,
                ArchivedAtUtc = null,
            }
        );
    }

    /// <summary>
    /// Registra que llegó un mensaje nuevo al hilo. Falla en vez de reabrir silenciosamente un
    /// hilo archivado — reabrir es una decisión de negocio explícita que esta fase no modela
    /// (nada la dispara todavía).
    /// </summary>
    public Result AppendMessage(DateTime receivedAtUtc)
    {
        if (Status == EmailThreadStatus.Archived)
            return Result.Failure(new Error("EmailThread.Archived", "Cannot append a message to an archived thread."));

        LastMessageAtUtc = receivedAtUtc;
        MessageCount++;
        return Result.Success();
    }

    /// <summary>
    /// Idempotente: archivar un hilo ya archivado no pisa el <see cref="ArchivedAtUtc"/> original.
    /// El reloj lo posee el aggregate (no un parámetro del caller) — mismo patrón que
    /// <see cref="Projections.CustomerEmailAddress"/> usa en <c>SoftDelete</c>/<c>Reactivate</c>
    /// dentro de este mismo servicio.
    /// </summary>
    public void Archive()
    {
        if (Status == EmailThreadStatus.Archived)
            return;

        Status = EmailThreadStatus.Archived;
        ArchivedAtUtc = DateTime.UtcNow;
    }

    private static Error? Validate(Guid tenantId, Guid customerId, string subject, string? providerThreadId)
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
