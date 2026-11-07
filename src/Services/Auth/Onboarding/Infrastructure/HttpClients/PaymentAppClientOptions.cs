namespace TaxVision.Auth.Onboarding.Infrastructure.HttpClients;

public sealed class PaymentAppClientOptions
{
    public const string SectionName = "Auth:PaymentApp";

    public string BaseUrl { get; set; } = "http://localhost:5430";
}
