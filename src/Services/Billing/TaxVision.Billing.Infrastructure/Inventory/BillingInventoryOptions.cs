namespace TaxVision.Billing.Infrastructure.Inventory;

/// <summary>URL base del servicio Inventory (destino del POST interno commit-sale al emitir la factura).</summary>
public sealed class BillingInventoryOptions
{
    public const string SectionName = "Billing:Inventory";

    // Puerto de Inventory en la flota local (scripts/start-fleet.ps1). En Docker lo pisa
    // Billing__Inventory__BaseUrl.
    public string BaseUrl { get; set; } = "http://localhost:5490";
}
