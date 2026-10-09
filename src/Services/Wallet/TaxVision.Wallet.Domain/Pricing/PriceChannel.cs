namespace TaxVision.Wallet.Domain.Pricing;

/// <summary>Canal facturable (00_Plan §4). La unidad depende del canal (segmento SMS, destinatario email…),
/// pero el precio en el catálogo es por unidad del canal.</summary>
public enum PriceChannel
{
    Email = 1,
    Sms = 2,
    Push = 3,
    WhatsApp = 4,
}
