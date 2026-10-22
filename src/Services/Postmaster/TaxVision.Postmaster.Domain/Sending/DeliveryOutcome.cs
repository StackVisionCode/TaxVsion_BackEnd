namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// Qué pasó con el correo de un destinatario, en nuestro idioma. Avanza y no retrocede; el orden lo
/// decide <see cref="ValueObjects.DeliveryOutcomePrecedence"/>.
/// </summary>
public enum DeliveryOutcome
{
    Accepted,
    Deferred,
    Delivered,
    SoftBounced,
    HardBounced,
    Rejected,
    Complained,
}
