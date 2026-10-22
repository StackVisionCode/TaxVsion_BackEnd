using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>
/// El estado que ve la persona en "Email activity". Une el resultado del envío con lo que contó el
/// proveedor después, en un solo valor. Los filtros usan los mismos nombres.
/// </summary>
public static class SentMessageActivityStatus
{
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Deferred = "Deferred";
    public const string Delivered = "Delivered";
    public const string Bounced = "Bounced";
    public const string Complained = "Complained";
    public const string Failed = "Failed";
    public const string Suppressed = "Suppressed";

    public static string Of(SentMessage message) =>
        message.Status switch
        {
            SentMessageStatus.Queued or SentMessageStatus.Sending => Sending,
            SentMessageStatus.Failed or SentMessageStatus.ProviderNotConfigured => Failed,
            SentMessageStatus.Suppressed => Suppressed,
            _ => message.DeliverySummary switch
            {
                DeliveryOutcome.Complained => Complained,
                DeliveryOutcome.HardBounced or DeliveryOutcome.SoftBounced or DeliveryOutcome.Rejected => Bounced,
                DeliveryOutcome.Deferred => Deferred,
                DeliveryOutcome.Delivered => Delivered,
                // Aceptado por el relay y sin noticias todavía.
                _ => Sent,
            },
        };
}
