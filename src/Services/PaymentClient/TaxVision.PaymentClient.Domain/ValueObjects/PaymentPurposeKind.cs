namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>Por qué el tenant le está cobrando al taxpayer — puramente informativo para el
/// tenant y el taxpayer, el dominio no ramifica lógica por esto.</summary>
public enum PaymentPurposeKind
{
    InvoicePayment = 1,
    DepositPayment = 2,
    RetainerPayment = 3,
    RefundIssuance = 4,
    Other = 5,
}
