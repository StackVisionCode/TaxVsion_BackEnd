namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Ciclo de vida del documento factura. Reemplaza el status string libre del CRM legado.</summary>
public enum InvoiceStatus
{
    Draft,
    Issued,
    Sent,
    PartiallyPaid,
    Paid,
    Voided,
}

/// <summary>Estado del enlace de cobro (ancla estable en PaymentClient) asociado a la factura.</summary>
public enum InvoicePaymentLinkStatus
{
    Active,
    Superseded,
    Revoked,
}

/// <summary>Estado del comprobante de pago verificable.</summary>
public enum ReceiptStatus
{
    Active,
    Void,
    Refunded,
}

/// <summary>Medio de pago aplicado a la factura.</summary>
public enum PaymentMethod
{
    Online,
    Card,
    Cash,
    Check,
    BankTransfer,
    Other,
}

/// <summary>Tipo de descuento a nivel de factura.</summary>
public enum DiscountType
{
    Percentage,
    Fixed,
}

/// <summary>Cómo se liquidó una factura de onboarding. Ortogonal al <see cref="InvoiceStatus"/>:
/// distingue "pagada en efectivo/tarjeta" de "cubierta por código" para la trazabilidad financiera.
/// - <see cref="Paid"/>: cubierta 100% por un pago real (sin descuento).
/// - <see cref="Mixed"/>: descuento parcial por código + pago real del neto (net &gt; 0).
/// - <see cref="FullyCoveredByCode"/>: descuento del 100% — total $0, sin Payment.</summary>
public enum SettlementType
{
    Paid,
    Mixed,
    FullyCoveredByCode,
}

/// <summary>Tipo de beneficio detrás de una línea de ajuste (descuento) de la factura. Espeja los
/// beneficios de Growth aplicados en el onboarding; se mantienen separados (no se fusionan).</summary>
public enum InvoiceAdjustmentType
{
    Referral,
    Promo,
    Gift,
}

/// <summary>Política de reinicio de la numeración server-side por tenant.</summary>
public enum NumberResetPolicy
{
    None,
    Yearly,
    Monthly,
}
