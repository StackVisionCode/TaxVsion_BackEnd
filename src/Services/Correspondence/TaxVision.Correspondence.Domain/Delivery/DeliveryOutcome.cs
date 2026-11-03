namespace TaxVision.Correspondence.Domain.Delivery;

/// <summary>
/// Resultado de entrega de UN destinatario de un correo enviado desde el CRM, tal como lo correlaciona
/// Postmaster. Delayed es temporal (el servidor sigue intentando); NotDelivered es final.
/// </summary>
public enum DeliveryOutcome
{
    Delivered = 0,
    Delayed = 1,
    NotDelivered = 2,
}
