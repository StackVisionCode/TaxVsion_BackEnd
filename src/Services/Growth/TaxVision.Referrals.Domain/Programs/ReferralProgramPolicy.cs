using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Domain.Programs;

/// <summary>
/// Snapshot versionable de las reglas comerciales. El default califica únicamente el primer
/// PaymentApp exitoso, espera 30 días, limita a 10 rewards por referrer y año calendario y
/// premia al REFERIDOR con un porcentaje del plan pagado (<see cref="RewardBasisPoints"/>, con
/// tope opcional) acreditado en su Wallet (<see cref="ReferralRewardType.WalletCredit"/>).
///
/// Solo se beneficia el que refiere: el referido no recibe ningún descuento ni premio.
/// </summary>
public sealed class ReferralProgramPolicy
{
    public const int DefaultAttributionWindowDays = 90;
    public const int DefaultWaitingPeriodDays = 30;
    public const int DefaultMaximumRewardsPerCalendarYear = 10;
    public const string DefaultRewardDefinitionKey = "referrals.tenant-to-tenant.standard";
    public const int DefaultRewardBasisPoints = 1_000;
    public const long DefaultRewardCapCents = 5_000;
    public const string DefaultRewardCurrency = "USD";

    public int AttributionWindowDays { get; private set; }
    public QualifyingPaymentSource PaymentSource { get; private set; }
    public QualifyingEventRule QualifyingEvent { get; private set; }
    public long MinimumPaymentAmountCents { get; private set; }
    public string? MinimumPaymentCurrency { get; private set; }
    public int WaitingPeriodDays { get; private set; }
    public int MaximumRewardsPerReferrerPerCalendarYear { get; private set; }
    public ReferralRewardType RewardType { get; private set; }
    public string RewardDefinitionKey { get; private set; } = default!;

    /// <summary>Porcentaje (puntos básicos, 10000 = 100 %) del plan pagado que gana el referidor. 0 si el premio no es WalletCredit.</summary>
    public int RewardBasisPoints { get; private set; }

    /// <summary>Tope del premio por referido en centavos; null = sin tope.</summary>
    public long? RewardCapCents { get; private set; }

    /// <summary>Moneda del premio (USD: el Wallet es USD-only). Null si el premio no es WalletCredit.</summary>
    public string? RewardCurrency { get; private set; }

    private ReferralProgramPolicy() { }

    public static ReferralProgramPolicy TenantToTenantDefaults() =>
        new()
        {
            AttributionWindowDays = DefaultAttributionWindowDays,
            PaymentSource = QualifyingPaymentSource.PaymentApp,
            QualifyingEvent = QualifyingEventRule.FirstSuccessfulPayment,
            MinimumPaymentAmountCents = 1,
            MinimumPaymentCurrency = null,
            WaitingPeriodDays = DefaultWaitingPeriodDays,
            MaximumRewardsPerReferrerPerCalendarYear = DefaultMaximumRewardsPerCalendarYear,
            RewardType = ReferralRewardType.WalletCredit,
            RewardDefinitionKey = DefaultRewardDefinitionKey,
            RewardBasisPoints = DefaultRewardBasisPoints,
            RewardCapCents = DefaultRewardCapCents,
            RewardCurrency = DefaultRewardCurrency,
        };

    public static Result<ReferralProgramPolicy> CreateTenantToTenant(
        int attributionWindowDays,
        long minimumPaymentAmountCents,
        string? minimumPaymentCurrency,
        int waitingPeriodDays,
        int maximumRewardsPerReferrerPerCalendarYear,
        ReferralRewardType rewardType,
        string rewardDefinitionKey,
        int rewardBasisPoints = 0,
        long? rewardCapCents = null,
        string? rewardCurrency = null
    )
    {
        var validation = Validate(
            attributionWindowDays,
            minimumPaymentAmountCents,
            minimumPaymentCurrency,
            waitingPeriodDays,
            maximumRewardsPerReferrerPerCalendarYear,
            rewardType,
            rewardDefinitionKey,
            rewardBasisPoints,
            rewardCapCents,
            rewardCurrency
        );
        if (validation.IsFailure)
            return Result.Failure<ReferralProgramPolicy>(validation.Error);

        return Result.Success(
            new ReferralProgramPolicy
            {
                AttributionWindowDays = attributionWindowDays,
                PaymentSource = QualifyingPaymentSource.PaymentApp,
                QualifyingEvent = QualifyingEventRule.FirstSuccessfulPayment,
                MinimumPaymentAmountCents = minimumPaymentAmountCents,
                MinimumPaymentCurrency = NormalizeCurrency(minimumPaymentCurrency),
                WaitingPeriodDays = waitingPeriodDays,
                MaximumRewardsPerReferrerPerCalendarYear = maximumRewardsPerReferrerPerCalendarYear,
                RewardType = rewardType,
                RewardDefinitionKey = rewardDefinitionKey.Trim(),
                RewardBasisPoints = rewardBasisPoints,
                RewardCapCents = rewardCapCents,
                RewardCurrency = NormalizeCurrency(rewardCurrency),
            }
        );
    }

    /// <summary>
    /// Permite modelar y persistir un borrador taxpayer-to-taxpayer, pero
    /// <see cref="ReferralProgram.Activate"/> impide habilitarlo en producción.
    /// </summary>
    public static Result<ReferralProgramPolicy> CreateTaxpayerToTaxpayerDraft(
        int attributionWindowDays,
        long minimumPaymentAmountCents,
        string? minimumPaymentCurrency,
        int waitingPeriodDays,
        int maximumRewardsPerReferrerPerCalendarYear,
        ReferralRewardType rewardType,
        string rewardDefinitionKey
    )
    {
        var validation = Validate(
            attributionWindowDays,
            minimumPaymentAmountCents,
            minimumPaymentCurrency,
            waitingPeriodDays,
            maximumRewardsPerReferrerPerCalendarYear,
            rewardType,
            rewardDefinitionKey,
            rewardBasisPoints: 0,
            rewardCapCents: null,
            rewardCurrency: null
        );
        if (validation.IsFailure)
            return Result.Failure<ReferralProgramPolicy>(validation.Error);

        return Result.Success(
            new ReferralProgramPolicy
            {
                AttributionWindowDays = attributionWindowDays,
                PaymentSource = QualifyingPaymentSource.PaymentClient,
                QualifyingEvent = QualifyingEventRule.FirstSuccessfulPayment,
                MinimumPaymentAmountCents = minimumPaymentAmountCents,
                MinimumPaymentCurrency = NormalizeCurrency(minimumPaymentCurrency),
                WaitingPeriodDays = waitingPeriodDays,
                MaximumRewardsPerReferrerPerCalendarYear = maximumRewardsPerReferrerPerCalendarYear,
                RewardType = rewardType,
                RewardDefinitionKey = rewardDefinitionKey.Trim(),
            }
        );
    }

    public bool MeetsMinimum(long amountCents, string currency)
    {
        if (amountCents < MinimumPaymentAmountCents)
            return false;

        return MinimumPaymentCurrency is null
            || string.Equals(MinimumPaymentCurrency, currency, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Premio del referidor para <see cref="ReferralRewardType.WalletCredit"/>:
    /// <c>min(pagado × basisPoints / 10000, tope)</c>, redondeado hacia abajo al centavo. Cero para
    /// los demás tipos (no monetarios). El monto se congela en el <c>ReferralRewardCase</c>.
    /// </summary>
    public long CalculateRewardCents(long pricePaidCents)
    {
        if (RewardType != ReferralRewardType.WalletCredit || pricePaidCents <= 0 || RewardBasisPoints <= 0)
            return 0;

        var reward = pricePaidCents * RewardBasisPoints / 10_000;
        return RewardCapCents is { } cap && reward > cap ? cap : reward;
    }

    private static Result Validate(
        int attributionWindowDays,
        long minimumPaymentAmountCents,
        string? minimumPaymentCurrency,
        int waitingPeriodDays,
        int maximumRewardsPerReferrerPerCalendarYear,
        ReferralRewardType rewardType,
        string rewardDefinitionKey,
        int rewardBasisPoints,
        long? rewardCapCents,
        string? rewardCurrency
    )
    {
        if (rewardType == ReferralRewardType.WalletCredit)
        {
            if (rewardBasisPoints is < 1 or > 10_000)
            {
                return Result.Failure(
                    new Error(
                        "ReferralPolicy.InvalidRewardPercentage",
                        "RewardBasisPoints must be between 1 and 10000 when RewardType is WalletCredit."
                    )
                );
            }

            if (rewardCapCents is <= 0)
            {
                return Result.Failure(
                    new Error("ReferralPolicy.InvalidRewardCap", "RewardCapCents must be greater than zero when supplied.")
                );
            }

            if (string.IsNullOrWhiteSpace(rewardCurrency) || rewardCurrency.Trim().Length != 3)
            {
                return Result.Failure(
                    new Error(
                        "ReferralPolicy.InvalidRewardCurrency",
                        "RewardCurrency is required and must be a 3-letter ISO code when RewardType is WalletCredit."
                    )
                );
            }
        }
        else if (rewardBasisPoints != 0 || rewardCapCents is not null || rewardCurrency is not null)
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.RewardAmountNotApplicable",
                    "RewardBasisPoints, RewardCapCents and RewardCurrency only apply when RewardType is WalletCredit."
                )
            );
        }

        if (attributionWindowDays is < 1 or > 730)
        {
            return Result.Failure(
                new Error("ReferralPolicy.InvalidAttributionWindow", "AttributionWindowDays must be between 1 and 730.")
            );
        }

        if (minimumPaymentAmountCents < 0)
        {
            return Result.Failure(
                new Error("ReferralPolicy.InvalidMinimumPayment", "Minimum payment cannot be negative.")
            );
        }

        if (
            minimumPaymentCurrency is not null
            && (string.IsNullOrWhiteSpace(minimumPaymentCurrency) || minimumPaymentCurrency.Trim().Length != 3)
        )
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidMinimumCurrency",
                    "Minimum payment currency must be a 3-letter ISO code when supplied."
                )
            );
        }

        if (waitingPeriodDays is < 0 or > 365)
        {
            return Result.Failure(
                new Error("ReferralPolicy.InvalidWaitingPeriod", "WaitingPeriodDays must be between 0 and 365.")
            );
        }

        if (maximumRewardsPerReferrerPerCalendarYear is < 1 or > 1000)
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidRewardLimit",
                    "Maximum rewards per referrer per calendar year must be between 1 and 1000."
                )
            );
        }

        if (!Enum.IsDefined(rewardType))
        {
            return Result.Failure(new Error("ReferralPolicy.InvalidRewardType", "RewardType is not supported."));
        }

        if (string.IsNullOrWhiteSpace(rewardDefinitionKey) || rewardDefinitionKey.Trim().Length > 100)
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidRewardDefinition",
                    "RewardDefinitionKey is required and must be 100 characters or fewer."
                )
            );
        }

        return Result.Success();
    }

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
}
