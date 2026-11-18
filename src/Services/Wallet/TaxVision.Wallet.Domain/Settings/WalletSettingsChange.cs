using BuildingBlocks.Domain;

namespace TaxVision.Wallet.Domain.Settings;

public enum WalletSettingsAction
{
    AutoRechargeEnabled = 1,
    AutoRechargeDisabled = 2,
    AutoRechargePaused = 3,
    LowBalanceThresholdChanged = 4,
}

/// <summary>
/// Historia append-only de los ajustes del monedero. Cada aceptación de la recarga automática deja aquí su texto,
/// versión e IP, aunque el ajuste vigente se pise después: es el registro del consentimiento.
/// </summary>
public sealed class WalletSettingsChange : TenantEntity
{
    public const int MaxDetailLength = 500;

    private WalletSettingsChange() { }

    public DateTime AtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién cambió el ajuste; null = el sistema.")]
    public Guid? UserId { get; private set; }

    public WalletSettingsAction Action { get; private set; }
    public string Detail { get; private set; } = string.Empty;
    public string? ConsentTextVersion { get; private set; }
    public string? ConsentText { get; private set; }
    public string? IpAddress { get; private set; }

    internal static WalletSettingsChange Record(
        Guid tenantId,
        WalletSettingsAction action,
        Guid? userId,
        DateTime atUtc,
        string detail,
        ConsentGrant? consent = null
    )
    {
        var change = new WalletSettingsChange
        {
            Id = Guid.NewGuid(),
            AtUtc = atUtc,
            UserId = userId,
            Action = action,
            Detail = detail.Length <= MaxDetailLength ? detail : detail[..MaxDetailLength],
            ConsentTextVersion = consent?.TextVersion,
            ConsentText = consent?.Text,
            IpAddress = consent?.IpAddress,
        };
        change.SetTenant(tenantId);
        return change;
    }
}
