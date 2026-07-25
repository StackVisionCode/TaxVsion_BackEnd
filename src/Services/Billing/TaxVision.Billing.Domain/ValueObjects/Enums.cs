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

/// <summary>Política de reinicio de la numeración server-side por tenant.</summary>
public enum NumberResetPolicy
{
    None,
    Yearly,
    Monthly,
}
