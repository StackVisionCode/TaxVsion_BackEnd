using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Settings;

/// <summary>
/// Ajustes del monedero, uno por oficina: umbral de saldo bajo y recarga automática con consentimiento expreso
/// (D4). Lleva las banderas que hacen que cada aviso salga una vez: el de saldo bajo por cruce y el de tope por día
/// de la oficina. Cada cambio hecho por una persona o por una pausa devuelve su <see cref="WalletSettingsChange"/>.
/// </summary>
public sealed class WalletSettings : AggregateRoot
{
    public const long DefaultLowBalanceThresholdMicros = 5_000_000;
    public const long MaxThresholdMicros = 1_000_000_000;
    public const int DailyRechargeLimit = 3;
    public const long MinAmountCents = 1_000;
    public const long MaxAmountCents = 100_000;

    private WalletSettings() { }

    /// <summary>0 = sin aviso de saldo bajo.</summary>
    public long LowBalanceThresholdMicros { get; private set; } = DefaultLowBalanceThresholdMicros;

    /// <summary>El aviso de este cruce ya salió; se rearma al volver al umbral o arriba.</summary>
    public bool LowBalanceAlertSent { get; private set; }

    public AutoRechargeStatus AutoRechargeStatus { get; private set; } = AutoRechargeStatus.Off;
    public AutoRechargePauseReason? AutoRechargePausedReason { get; private set; }
    public long AutoRechargeAmountCents { get; private set; }
    public long AutoRechargeThresholdMicros { get; private set; }
    public string? AutoRechargeProvider { get; private set; }
    public string? AutoRechargeMethodReference { get; private set; }
    public string? AutoRechargeMethodLabel { get; private set; }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién aceptó el cobro automático; dato legal.")]
    public Guid? ConsentAcceptedByUserId { get; private set; }
    public DateTime? ConsentAcceptedAtUtc { get; private set; }
    public string? ConsentIpAddress { get; private set; }
    public string? ConsentTextVersion { get; private set; }
    public string? ConsentText { get; private set; }

    /// <summary>Día de la oficina del contador de recargas automáticas.</summary>
    public DateOnly? RechargesDay { get; private set; }
    public int RechargesOnDay { get; private set; }

    /// <summary>Día de la oficina en que ya se avisó el tope diario.</summary>
    public DateOnly? DailyLimitNoticeDay { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Token de concurrencia optimista (<c>rowversion</c>).</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>El método autorizado vigente, o null si nunca se autorizó uno.</summary>
    public AutoRechargeMethod? AuthorizedMethod =>
        AutoRechargeMethod.Create(AutoRechargeProvider, AutoRechargeMethodReference, AutoRechargeMethodLabel)
            is { IsSuccess: true } method
            ? method.Value
            : null;

    public static WalletSettings CreateDefault(Guid tenantId)
    {
        var settings = new WalletSettings { Id = Guid.NewGuid(), UpdatedAtUtc = DateTime.UtcNow };
        settings.SetTenant(tenantId);
        return settings;
    }

    public int RechargesOn(DateOnly officeDay) => RechargesDay == officeDay ? RechargesOnDay : 0;

    /// <summary>Activa y bajo su umbral: le tocaría recargar (si no hay otra en curso ni se llegó al tope).</summary>
    public bool NeedsAutoRecharge(long availableMicros) =>
        AutoRechargeStatus == AutoRechargeStatus.Active && availableMicros < AutoRechargeThresholdMicros;

    public Result<WalletSettingsChange> ChangeLowBalanceThreshold(long thresholdMicros, Guid userId, DateTime nowUtc)
    {
        if (thresholdMicros is < 0 or > MaxThresholdMicros)
            return Result.Failure<WalletSettingsChange>(WalletSettingsErrors.ThresholdOutOfRange);

        var previous = LowBalanceThresholdMicros;
        LowBalanceThresholdMicros = thresholdMicros;
        // Umbral nuevo, cruce nuevo: el próximo cambio de saldo decide con este valor.
        LowBalanceAlertSent = false;
        UpdatedAtUtc = nowUtc;
        var detail = $"{WalletAmounts.FromMicros(previous)} → {WalletAmounts.FromMicros(thresholdMicros)}";
        return Result.Success(Record(WalletSettingsAction.LowBalanceThresholdChanged, userId, nowUtc, detail));
    }

    /// <summary>Enciende (o reactiva) la recarga con el consentimiento recién aceptado. Todo cambio pasa por aquí.</summary>
    public Result<WalletSettingsChange> EnableAutoRecharge(
        long amountCents,
        long thresholdMicros,
        AutoRechargeMethod method,
        ConsentGrant consent
    )
    {
        if (amountCents is < MinAmountCents or > MaxAmountCents)
            return Result.Failure<WalletSettingsChange>(WalletSettingsErrors.AmountNotAllowed);
        if (thresholdMicros is <= 0 or > MaxThresholdMicros)
            return Result.Failure<WalletSettingsChange>(WalletSettingsErrors.AutoRechargeThresholdOutOfRange);
        if (string.IsNullOrWhiteSpace(consent.Text) || string.IsNullOrWhiteSpace(consent.TextVersion))
            return Result.Failure<WalletSettingsChange>(WalletSettingsErrors.ConsentRequired);

        AutoRechargeStatus = AutoRechargeStatus.Active;
        AutoRechargePausedReason = null;
        AutoRechargeAmountCents = amountCents;
        AutoRechargeThresholdMicros = thresholdMicros;
        AutoRechargeProvider = method.Provider;
        AutoRechargeMethodReference = method.Reference;
        AutoRechargeMethodLabel = method.Label;
        ConsentAcceptedByUserId = consent.UserId;
        ConsentAcceptedAtUtc = consent.AcceptedAtUtc;
        ConsentIpAddress = consent.IpAddress;
        ConsentTextVersion = consent.TextVersion;
        ConsentText = consent.Text;
        UpdatedAtUtc = consent.AcceptedAtUtc;

        var detail =
            $"{WalletAmounts.FromCents(amountCents)} when below {WalletAmounts.FromMicros(thresholdMicros)} · {method.Label}";
        return Result.Success(
            Record(WalletSettingsAction.AutoRechargeEnabled, consent.UserId, consent.AcceptedAtUtc, detail, consent)
        );
    }

    /// <summary>Apaga la recarga. Null si ya estaba apagada (no deja historia).</summary>
    public WalletSettingsChange? DisableAutoRecharge(Guid userId, DateTime nowUtc)
    {
        if (AutoRechargeStatus == AutoRechargeStatus.Off)
            return null;
        AutoRechargeStatus = AutoRechargeStatus.Off;
        AutoRechargePausedReason = null;
        UpdatedAtUtc = nowUtc;
        return Record(WalletSettingsAction.AutoRechargeDisabled, userId, nowUtc, "Auto-recharge turned off");
    }

    /// <summary>Pausa tras un cobro fallido. Null si no estaba activa.</summary>
    public WalletSettingsChange? PauseAutoRecharge(AutoRechargePauseReason reason, DateTime nowUtc)
    {
        if (AutoRechargeStatus != AutoRechargeStatus.Active)
            return null;
        AutoRechargeStatus = AutoRechargeStatus.Paused;
        AutoRechargePausedReason = reason;
        UpdatedAtUtc = nowUtc;
        var detail =
            reason == AutoRechargePauseReason.MethodUnavailable ? "Payment method unavailable" : "Payment failed";
        return Record(WalletSettingsAction.AutoRechargePaused, null, nowUtc, detail);
    }

    /// <summary>
    /// Revisa el saldo tras un cambio: aviso de saldo bajo una vez por cruce y, si toca, una recarga automática
    /// (cuenta contra el tope del día de la oficina). No publica nada: el handler guarda y luego publica.
    /// </summary>
    public BalanceCheck CheckBalance(long availableMicros, DateOnly officeToday, bool pendingAutomatic)
    {
        var alert = false;
        var changed = false;
        if (LowBalanceAlertSent && availableMicros >= LowBalanceThresholdMicros)
        {
            LowBalanceAlertSent = false;
            changed = true;
        }
        else if (!LowBalanceAlertSent && availableMicros < LowBalanceThresholdMicros)
        {
            LowBalanceAlertSent = true;
            alert = changed = true;
        }

        var decision = DecideAutoRecharge(availableMicros, officeToday, pendingAutomatic);
        changed |= decision != AutoRechargeDecision.None;
        if (changed)
            UpdatedAtUtc = DateTime.UtcNow;
        return new BalanceCheck(alert, decision, changed);
    }

    private AutoRechargeDecision DecideAutoRecharge(long availableMicros, DateOnly officeToday, bool pendingAutomatic)
    {
        if (!NeedsAutoRecharge(availableMicros) || pendingAutomatic)
            return AutoRechargeDecision.None;

        if (RechargesOn(officeToday) >= DailyRechargeLimit)
        {
            if (DailyLimitNoticeDay == officeToday)
                return AutoRechargeDecision.None;
            DailyLimitNoticeDay = officeToday;
            return AutoRechargeDecision.DailyLimitReached;
        }

        RechargesOnDay = RechargesOn(officeToday) + 1;
        RechargesDay = officeToday;
        return AutoRechargeDecision.Recharge;
    }

    private WalletSettingsChange Record(
        WalletSettingsAction action,
        Guid? userId,
        DateTime atUtc,
        string detail,
        ConsentGrant? consent = null
    ) => WalletSettingsChange.Record(TenantId, action, userId, atUtc, detail, consent);
}
