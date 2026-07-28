namespace TaxVision.Billing.Infrastructure.Documents;

/// <summary>URL base del servicio Documents (destino del POST de generación).</summary>
public sealed class BillingDocumentsOptions
{
    public const string SectionName = "Billing:Documents";
    public string BaseUrl { get; set; } = "http://localhost:5450";
}
