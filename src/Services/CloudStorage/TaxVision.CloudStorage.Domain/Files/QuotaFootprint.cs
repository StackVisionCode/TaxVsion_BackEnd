namespace TaxVision.CloudStorage.Domain.Files;

/// <summary>Qué cuenta un archivo contra la cuota del tenant (CST-5).</summary>
public enum QuotaFootprint
{
    None,
    Reserved,
    Used,
}
