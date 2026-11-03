using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>Qué hacer con el EFIN en un guardado: dejarlo como está, borrarlo o reemplazarlo por uno ya cifrado.</summary>
public sealed class EfinChange
{
    public static readonly EfinChange Keep = new(EfinChangeKind.Keep, null);
    public static readonly EfinChange Clear = new(EfinChangeKind.Clear, null);

    private EfinChange(EfinChangeKind kind, EncryptedEfin? newValue)
    {
        Kind = kind;
        NewValue = newValue;
    }

    public EfinChangeKind Kind { get; }
    public EncryptedEfin? NewValue { get; }

    public static EfinChange Replace(EncryptedEfin encrypted) => new(EfinChangeKind.Replace, encrypted);
}
