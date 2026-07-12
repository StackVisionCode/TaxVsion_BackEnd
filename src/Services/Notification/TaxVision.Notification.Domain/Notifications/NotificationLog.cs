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
}

/// <summary>
/// Historial de notificaciones por tenant. Nunca almacena el cuerpo completo
/// (puede contener tokens/OTP): solo canal, destinatario, plantilla y estado.
/// </summary>
public sealed class NotificationLog : TenantEntity
{
    private NotificationLog() { }

    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string TemplateKey { get; private set; } = default!;
    public NotificationStatus Status { get; private set; }
    public string? Error { get; private set; }
    public Guid? RelatedEventId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    public static Result<NotificationLog> Create(
        Guid tenantId,
        NotificationChannel channel,
        string recipient,
        string subject,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
