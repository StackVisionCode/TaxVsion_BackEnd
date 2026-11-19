using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Settings;

/// <summary>Estado de la recarga automática. Viene apagada; solo la enciende quien acepta el consentimiento.</summary>
public enum AutoRechargeStatus
{
    Off = 1,
    Active = 2,

    /// <summary>Un cobro falló o el método dejó de servir: no reintenta sola hasta que la oficina la reactive.</summary>
    Paused = 3,
}

public enum AutoRechargePauseReason
{
    PaymentFailed = 1,
    MethodUnavailable = 2,
}

/// <summary>Qué hacer con la recarga automática tras un cambio de saldo.</summary>
public enum AutoRechargeDecision
{
    None = 0,
    Recharge = 1,

    /// <summary>Hacía falta recargar pero ya se llegó al tope del día (se avisa una vez por día).</summary>
    DailyLimitReached = 2,
}

/// <summary>Resultado de revisar el saldo: avisos a publicar y si el agregado cambió (hay que guardarlo).</summary>
public sealed record BalanceCheck(bool LowBalanceAlert, AutoRechargeDecision AutoRecharge, bool Changed);

/// <summary>Método guardado que la oficina autorizó para la recarga automática.</summary>
public sealed record AutoRechargeMethod
{
    private AutoRechargeMethod(string provider, string reference, string label)
    {
        Provider = provider;
        Reference = reference;
        Label = label;
    }

    public const int MaxProviderLength = 32;
    public const int MaxReferenceLength = 200;
    public const int MaxLabelLength = 100;

    public string Provider { get; }
    public string Reference { get; }
    public string Label { get; }

    public static Result<AutoRechargeMethod> Create(string? provider, string? reference, string? label)
    {
        var p = provider?.Trim();
        var r = reference?.Trim();
        var l = label?.Trim();
        if (
            string.IsNullOrEmpty(p)
            || string.IsNullOrEmpty(r)
            || string.IsNullOrEmpty(l)
            || p.Length > MaxProviderLength
            || r.Length > MaxReferenceLength
            || l.Length > MaxLabelLength
        )
            return Result.Failure<AutoRechargeMethod>(WalletSettingsErrors.MethodRequired);
        return Result.Success(new AutoRechargeMethod(p, r, l));
    }
}

/// <summary>Aceptación expresa del texto de la recarga automática: quién, cuándo, desde qué IP y qué texto.</summary>
public sealed record ConsentGrant(
    Guid UserId,
    DateTime AcceptedAtUtc,
    string? IpAddress,
    string TextVersion,
    string Text
);

public static class WalletSettingsErrors
{
    public static readonly Error ThresholdOutOfRange = new(
        "WalletSettings.ThresholdOutOfRange",
        "The threshold must be between $0 and $1,000."
    );
    public static readonly Error AutoRechargeThresholdOutOfRange = new(
        "WalletSettings.AutoRechargeThresholdOutOfRange",
        "The auto-recharge threshold must be greater than $0 and up to $1,000."
    );
    public static readonly Error AmountNotAllowed = new(
        "WalletSettings.AmountNotAllowed",
        "The auto-recharge amount must be between $10 and $1,000."
    );
    public static readonly Error MethodRequired = new(
        "WalletSettings.MethodRequired",
        "Choose a saved payment method for auto-recharge."
    );
    public static readonly Error ConsentRequired = new(
        "WalletSettings.ConsentRequired",
        "You must accept the auto-recharge authorization."
    );
    public static readonly Error ConsentVersionConflict = new(
        "WalletSettings.ConsentVersionConflict",
        "The authorization text changed. Review it and accept it again."
    );
}
