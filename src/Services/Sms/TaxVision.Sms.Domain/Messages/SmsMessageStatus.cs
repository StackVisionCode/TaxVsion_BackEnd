namespace TaxVision.Sms.Domain.Messages;

/// <summary>Estados canónicos de un intento de envío. Un adapter NUNCA filtra estados del proveedor
/// directamente: los mapea a este conjunto.</summary>
public enum SmsMessageStatus
{
    Pending,
    Accepted,
    Delivered,
    Failed,
    Undeliverable,
    Suppressed,

    // Se pidió el envío y no se sabe si salió (timeout): no se cobra hasta que el proveedor confirme.
    OutcomeUnknown,
}
