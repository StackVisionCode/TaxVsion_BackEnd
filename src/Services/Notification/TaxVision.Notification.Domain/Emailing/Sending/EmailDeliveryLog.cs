using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Sending;

/// <summary>Registro de un intento de entrega de un correo saliente (auditoría/tracking).</summary>
public sealed class EmailDeliveryLog : BaseEntity
{
    private EmailDeliveryLog() { }

    public Guid MessageId { get; private set; }
    public EmailStatus Status { get; private set; }
    public string? Detail { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }

    internal static EmailDeliveryLog Create(EmailStatus status, string? detail) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            Detail = detail is { Length: > 1024 } ? detail[..1024] : detail,
            AttemptedAtUtc = DateTime.UtcNow,
        };
}
