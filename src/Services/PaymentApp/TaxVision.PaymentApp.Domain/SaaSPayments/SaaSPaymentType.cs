namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Motivo de negocio por el que se generó el cobro SaaS. Reemplaza el
/// <c>PaymentType.PaymentTypeName: string</c> del CRM legado (ver auditoría §6.2.10).
/// </summary>
public enum SaaSPaymentType
{
    /// <summary>Cobro de renovación periódica de la suscripción base de un tenant.</summary>
    SubscriptionRenewal = 1,

    /// <summary>Cobro de renovación de un asiento (seat) adicional.</summary>
    SeatRenewal = 2,

    /// <summary>Cobro de renovación de un add-on contratado.</summary>
    AddOnRenewal = 3,

    /// <summary>Cargo generado por un upgrade de plan a mitad de ciclo.</summary>
    PlanChangeCharge = 4,

    /// <summary>Cargo por compra de seats adicionales fuera del ciclo de renovación.</summary>
    SeatsPurchaseCharge = 5,

    /// <summary>Cobro emitido para compensar un reembolso previamente aprobado.</summary>
    Refund = 6,

    /// <summary>Registro generado a raíz de un chargeback iniciado por el emisor de la tarjeta.</summary>
    ChargeBack = 7,
}
