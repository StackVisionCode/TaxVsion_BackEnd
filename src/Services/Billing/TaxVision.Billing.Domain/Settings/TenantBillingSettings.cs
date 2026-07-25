using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Settings;

/// <summary>Configuración de facturación por tenant: identidad de emisor por defecto, ajustes de
/// PDF y política de numeración. Una fila por tenant. SCAFFOLD B1: fábrica/actualización en B6.</summary>
public sealed class TenantBillingSettings : TenantEntity
{
    public IssuerSnapshot? DefaultIssuer { get; private set; }
    public string Template { get; private set; } = "classic";
    public string PageSize { get; private set; } = "Letter";
    public string Orientation { get; private set; } = "portrait";
    public bool ShowLogo { get; private set; } = true;
    public bool ShowFooter { get; private set; } = true;
    public bool ShowAttachments { get; private set; }
    public string NumberPrefix { get; private set; } = "INV";
    public NumberResetPolicy ResetPolicy { get; private set; } = NumberResetPolicy.Yearly;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private TenantBillingSettings() { }
}
