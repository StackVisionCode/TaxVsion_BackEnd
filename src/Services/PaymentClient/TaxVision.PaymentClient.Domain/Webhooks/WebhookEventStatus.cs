namespace TaxVision.PaymentClient.Domain.Webhooks;

public enum WebhookEventStatus
{
    Received = 1,
    Processing = 2,
    Applied = 3,
    Duplicate = 4,
    Rejected = 5,
    Failed = 6,
    Stale = 7,

    /// <summary>El proveedor confirmó un cobro que no se pudo aplicar (pago cerrado o monto distinto).
    /// Lo resuelve el dueño a mano; mismo nombre que en PaymentApp.</summary>
    NeedsReview = 8,
}
