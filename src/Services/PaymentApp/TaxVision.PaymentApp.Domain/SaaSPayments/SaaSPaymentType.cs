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

    /// <summary>PayFlow — primer pago de un onboarding pago-primero, antes de que el tenant
    /// exista. Único tipo que permite <c>TenantId=Guid.Empty</c> (ver <see cref="SaaSPayment.CreateForOnboarding"/>).</summary>
    OnboardingInitial = 8,

    /// <summary>Renovación/reactivación self-service de la suscripción base por HOSTED-CHECKOUT (redirect),
    /// para el tenant sin método en archivo cuya suscripción cayó en lapso (PastDue/GracePeriod/Suspended/
    /// Expired). Distinto de <see cref="SubscriptionRenewal"/> (cobro off-session periódico): éste se paga
    /// por checkout y al confirmarse reactiva la suscripción sin re-cobrar.</summary>
    SubscriptionRenewalCheckout = 9,

    /// <summary>Compra de un add-on por HOSTED-CHECKOUT (redirect), para el tenant sin método en archivo.
    /// Distinto de <see cref="AddOnRenewal"/> (cobro off-session periódico): éste se paga por checkout y al
    /// confirmarse activa el add-on sin re-cobrar.</summary>
    AddOnPurchaseCharge = 10,

    /// <summary>Upgrade de plan pagado por HOSTED-CHECKOUT (redirect), para el tenant sin método en archivo.
    /// Mismo resultado que <see cref="PlanChangeCharge"/> (el off-session): los dos cierran el mismo
    /// <c>PlanChangeRequest</c> de Subscription, solo cambia cómo se cobra.</summary>
    PlanChangeCheckout = 11,
}
