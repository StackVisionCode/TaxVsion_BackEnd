namespace TaxVision.Tenant.Infrastructure.Billing;

public sealed class BillingClientOptions
{
    public const string SectionName = "BillingClient";

    /// <summary>Base URL de Billing (puerto de la flota local). En Docker: http://billing-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5460";
}
