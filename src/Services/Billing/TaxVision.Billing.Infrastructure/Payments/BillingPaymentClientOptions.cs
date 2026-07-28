namespace TaxVision.Billing.Infrastructure.Payments;

/// <summary>URL base del servicio PaymentClient (destino del POST interno ensure-payable). La URL de
/// checkout la compone y devuelve PaymentClient — Billing no la arma.</summary>
public sealed class BillingPaymentClientOptions
{
    public const string SectionName = "Billing:PaymentClient";
    public string BaseUrl { get; set; } = "http://localhost:5175";
}
