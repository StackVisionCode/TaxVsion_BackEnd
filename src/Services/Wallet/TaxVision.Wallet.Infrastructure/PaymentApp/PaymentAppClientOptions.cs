namespace TaxVision.Wallet.Infrastructure.PaymentApp;

/// <summary>Base URL del servicio PaymentApp para el checkout de recarga (M2M). En Docker: http://payment-app-api:8080.</summary>
public sealed class PaymentAppClientOptions
{
    public const string SectionName = "Wallet:PaymentApp";

    public string BaseUrl { get; set; } = "http://localhost:5430";
}
