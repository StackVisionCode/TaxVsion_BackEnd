namespace TaxVision.Billing.Infrastructure.Inventory;

/// <summary>URL base del servicio Inventory (destino del POST interno commit-sale al emitir la factura).</summary>
public sealed class BillingInventoryOptions
{
    public const string SectionName = "Billing:Inventory";
    public string BaseUrl { get; set; } = "http://localhost:5180";
}
