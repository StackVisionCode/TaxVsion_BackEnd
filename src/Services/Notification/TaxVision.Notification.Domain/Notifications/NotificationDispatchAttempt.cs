using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Notifications;

/// <summary>
/// Estado por-canal de un intento de despacho de una notificación. Es entity hija de
/// <see cref="NotificationLog"/> — no se instancia sola: se crea siempre vía
/// <c>NotificationLog.AddDispatchAttempt</c>.
/// </summary>
/// <remarks>
/// Introducida en Notifications Fase 2. Coexiste con el estado agregado legacy de
/// <c>NotificationLog.Status</c> (Sent/Failed). Fases 3-4 pueblan estos rows; fases 6+ los usan
/// como fuente primaria (ver <c>Notifications_Service_Responsibility_Cleanup_Plan.md</c> §14).
/// </remarks>
public sealed class NotificationDispatchAttempt : TenantEntity
{
    private NotificationDispatchAttempt() { }

    public Guid NotificationLogId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationDispatchAttemptStatus Status { get; private set; }

    /// <summary>ID opaco del proveedor material (ej. <c>SentMessage.Id</c> de Postmaster).</summary>
    public string? ProviderMessageId { get; private set; }

    public DateTime QueuedAtUtc { get; private set; }
    public DateTime? LastEventAtUtc { get; private set; }
    public string? ErrorReason { get; private set; }

    /// <summary>Payload JSON adicional (headers de callback, error extendido, etc.).</summary>
    public string? Metadata { get; private set; }

    internal static NotificationDispatchAttempt Create(
        Guid tenantId,
        Guid notificationLogId,
        NotificationChannel channel,
        string? providerMessageId,
        DateTime queuedAtUtc
    )
    {
        var attempt = new NotificationDispatchAttempt
        {
            Id = Guid.NewGuid(),
            NotificationLogId = notificationLogId,
            Channel = channel,
            Status = NotificationDispatchAttemptStatus.Queued,
            ProviderMessageId = Truncate(providerMessageId, 200),
            QueuedAtUtc = queuedAtUtc,
        };
        attempt.SetTenant(tenantId);
        return attempt;
    }

    internal Result Transition(
        NotificationDispatchAttemptStatus newStatus,
        string? providerMessageId,
        string? errorReason,
        DateTime eventAtUtc
    )
    {
        if (!IsValidTransition(Status, newStatus))
        {
            return Result.Failure(
                new Error(
                    "NotificationDispatchAttempt.InvalidTransition",
                    $"Cannot transition dispatch attempt from {Status} to {newStatus}."
                )
            );
        }

        Status = newStatus;
        LastEventAtUtc = eventAtUtc;
        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            ProviderMessageId = Truncate(providerMessageId, 200);
        }
        if (
            newStatus
            is NotificationDispatchAttemptStatus.Failed
                or NotificationDispatchAttemptStatus.Bounced
                or NotificationDispatchAttemptStatus.ProviderNotConfigured
        )
        {
            ErrorReason = Truncate(errorReason, 500);
        }
        else if (newStatus is NotificationDispatchAttemptStatus.Sent or NotificationDispatchAttemptStatus.Delivered)
        {
            ErrorReason = null;
        }
        return Result.Success();
    }

    /// <summary>
    /// Transiciones permitidas — las terminales (Delivered / Bounced / Failed / Suppressed /
    /// ProviderNotConfigured) no pueden salir. Sent puede subir a Delivered/Bounced por webhook.
    /// </summary>
    private static bool IsValidTransition(
        NotificationDispatchAttemptStatus from,
        NotificationDispatchAttemptStatus to
    ) =>
        from switch
        {
            NotificationDispatchAttemptStatus.Queued => to
                is NotificationDispatchAttemptStatus.Sent
                    or NotificationDispatchAttemptStatus.Failed
                    or NotificationDispatchAttemptStatus.Suppressed
                    or NotificationDispatchAttemptStatus.ProviderNotConfigured,
            NotificationDispatchAttemptStatus.Sent => to
                is NotificationDispatchAttemptStatus.Delivered
                    or NotificationDispatchAttemptStatus.Bounced
                    or NotificationDispatchAttemptStatus.Failed,
            _ => false,
        };

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}

public enum NotificationDispatchAttemptStatus
{
    Queued,
    Sent,
    Delivered,
    Bounced,
    Failed,
    Suppressed,
    ProviderNotConfigured,
}
