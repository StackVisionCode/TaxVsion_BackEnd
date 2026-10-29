using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Notifications;

public enum NotificationChannel
{
    Email,
    Sms,
    InApp,
    Push,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,

    /// <summary>El proveedor confirmó que el servidor del destinatario lo aceptó.</summary>
    Delivered,

    /// <summary>Rebotó después de salir (dirección inexistente, buzón lleno…).</summary>
    Bounced,

    /// <summary>No había a dónde entregar (p. ej. push sin dispositivo registrado); no es un fallo.</summary>
    Skipped,
}

/// <summary>
/// Historial de notificaciones por tenant. No almacena el cuerpo completo y redacta
/// asuntos asociados a códigos o tokens sensibles.
/// </summary>
public sealed class NotificationLog : TenantEntity
{
    private readonly List<NotificationDispatchAttempt> _attempts = new();

    private NotificationLog() { }

    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Kind { get; private set; } = default!;
    public string TemplateKey { get; private set; } = default!;
    public NotificationStatus Status { get; private set; }
    public string? Error { get; private set; }
    public Guid? RelatedEventId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    /// <summary>
    /// Intentos por canal introducidos en Notifications Fase 2. Vacío hasta que los consumers
    /// (Fase 3+) empiecen a poblarlos. La existencia de esta colección no altera el estado
    /// agregado <see cref="Status"/>, que sigue actualizándose por <see cref="MarkSent"/> /
    /// <see cref="MarkFailed"/> como legacy hasta Fase 6.
    /// </summary>
    public IReadOnlyCollection<NotificationDispatchAttempt> Attempts => _attempts.AsReadOnly();

    public static Result<NotificationLog> Create(
        Guid tenantId,
        NotificationChannel channel,
        string recipient,
        string subject,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        string? kind = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<NotificationLog>(new Error("Notification.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(recipient))
            return Result.Failure<NotificationLog>(new Error("Notification.Recipient", "Recipient is required."));

        var log = new NotificationLog
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            Recipient = recipient.Trim(),
            Subject = Truncate(subject, 200),
            Kind = Truncate(kind ?? templateKey, 64),
            TemplateKey = Truncate(templateKey, 64),
            Status = NotificationStatus.Pending,
            RelatedEventId = relatedEventId,
            CorrelationId = correlationId is { Length: > 128 } ? correlationId[..128] : correlationId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        log.SetTenant(tenantId);
        return Result.Success(log);
    }

    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Status = NotificationStatus.Failed;
        Error = Truncate(error, 512);
    }

    // La razón va en Error para que el historial diga por qué no salió.
    public void MarkSkipped(string reason)
    {
        Status = NotificationStatus.Skipped;
        Error = Truncate(reason, 512);
    }

    /// <summary>Solo desde Sent: un fallo no se "arregla" con un aviso tardío del proveedor.</summary>
    public void MarkDelivered()
    {
        if (Status == NotificationStatus.Sent)
            Status = NotificationStatus.Delivered;
    }

    /// <summary>Desde Sent o Delivered: un rebote tardío (aceptó y después devolvió) es la verdad final.</summary>
    public void MarkBounced(string reason)
    {
        if (Status is not (NotificationStatus.Sent or NotificationStatus.Delivered))
            return;

        Status = NotificationStatus.Bounced;
        Error = Truncate(reason, 512);
    }

    /// <summary>
    /// Crea un nuevo <see cref="NotificationDispatchAttempt"/> para el canal indicado y lo agrega
    /// a la colección del aggregate. Introducido en Notifications Fase 2 — no se invoca todavía;
    /// los consumers empiezan a llamarlo en Fase 3.
    /// </summary>
    public NotificationDispatchAttempt AddDispatchAttempt(
        NotificationChannel channel,
        string? providerMessageId = null,
        DateTime? queuedAtUtc = null
    )
    {
        var attempt = NotificationDispatchAttempt.Create(
            TenantId,
            Id,
            channel,
            providerMessageId,
            queuedAtUtc ?? DateTime.UtcNow
        );
        _attempts.Add(attempt);
        return attempt;
    }

    /// <summary>
    /// Actualiza el estado de un attempt existente (ej. desde el callback de Postmaster). Todas
    /// las transiciones válidas están definidas en <see cref="NotificationDispatchAttempt"/>.
    /// </summary>
    public Result UpdateAttemptStatus(
        Guid attemptId,
        NotificationDispatchAttemptStatus newStatus,
        string? providerMessageId = null,
        string? errorReason = null,
        DateTime? eventAtUtc = null
    )
    {
        var attempt = _attempts.FirstOrDefault(a => a.Id == attemptId);
        if (attempt is null)
        {
            return Result.Failure(
                new Error("NotificationLog.AttemptNotFound", $"Dispatch attempt {attemptId} not found on log {Id}.")
            );
        }
        return attempt.Transition(newStatus, providerMessageId, errorReason, eventAtUtc ?? DateTime.UtcNow);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
