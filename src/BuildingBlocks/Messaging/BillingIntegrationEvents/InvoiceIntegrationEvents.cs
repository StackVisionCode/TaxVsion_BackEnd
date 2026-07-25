namespace BuildingBlocks.Messaging.BillingIntegrationEvents;

/// <summary>Publicado cuando una factura se emite (Draft → Issued).</summary>
public sealed record InvoiceIssuedIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.issued";
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required Guid CustomerId { get; init; }
    public required long TotalAmountCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime DueDateUtc { get; init; }
}

/// <summary>Publicado cuando una factura se envía. Notification lo consume para el email al cliente.</summary>
public sealed record InvoiceSentIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.sent";
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required Guid CustomerId { get; init; }
    public string? CustomerEmail { get; init; }
    public Guid? PdfFileId { get; init; }
    public string? PayUrl { get; init; }
    public required string PaymentMethod { get; init; }
}

/// <summary>Publicado cuando una factura queda totalmente pagada.</summary>
public sealed record InvoicePaidIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.paid";
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required Guid CustomerId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required string PaymentMethod { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required Guid ReceiptId { get; init; }
    public Guid? PaidPdfFileId { get; init; }
}

/// <summary>Publicado cuando se aplica un pago parcial.</summary>
public sealed record InvoicePartiallyPaidIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.partially_paid";
    public required Guid InvoiceId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required long AmountDueCents { get; init; }
    public required string Currency { get; init; }
    public required Guid ReceiptId { get; init; }
}

/// <summary>Publicado cuando un intento de cobro asociado a una factura falla.</summary>
public sealed record InvoicePaymentFailedIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.payment_failed";
    public required Guid InvoiceId { get; init; }
    public required string PaymentSource { get; init; }
    public required Guid PaymentId { get; init; }
    public required string FailureCode { get; init; }
}

/// <summary>Publicado cuando se registra un reembolso sobre una factura pagada.</summary>
public sealed record InvoiceRefundedIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.refunded";
    public required Guid InvoiceId { get; init; }
    public required string RefundReference { get; init; }
    public required long RefundAmountCents { get; init; }
    public required string Currency { get; init; }
    public Guid? ReceiptId { get; init; }
}

/// <summary>Publicado cuando una factura se anula.</summary>
public sealed record InvoiceVoidedIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.invoice.voided";
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Publicado cuando se emite un comprobante de pago. Notification lo consume para el email.</summary>
public sealed record ReceiptIssuedIntegrationEvent : BillingIntegrationEvent
{
    public override string EventType => "billing.receipt.issued";
    public required Guid ReceiptId { get; init; }
    public required string ReceiptNumber { get; init; }
    public required Guid InvoiceId { get; init; }
    public required Guid CustomerId { get; init; }
    public string? CustomerEmail { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public Guid? PdfFileId { get; init; }
    public string? VerifyUrl { get; init; }
}
