using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Domain.Programs;

/// <summary>
/// Snapshot versionable de las reglas comerciales. El default MVP califica únicamente
/// el primer PaymentApp exitoso, espera 30 días, limita a 10 rewards por referrer y año
/// calendario y solo solicita un beneficio no monetario a Subscription.
///
/// El beneficio del referido (RefereeBenefit*) es opcional y aparte del reward del
/// referrer: si <see cref="RefereeBenefitType"/> es null, el programa se comporta como
/// antes (solo premia a quien refiere). Si está seteado, <c>CreateReferralAttributionHandler</c>
/// emite un CodeDefinition de descuento propio para el referido al momento de atribuirse.
/// </summary>
public sealed class ReferralProgramPolicy
{
    public const int DefaultAttributionWindowDays = 90;
    public const int DefaultWaitingPeriodDays = 30;
    public const int DefaultMaximumRewardsPerCalendarYear = 10;
    public const string DefaultRewardDefinitionKey = "referrals.tenant-to-tenant.standard";
    public const int DefaultRefereeBenefitExpirationDays = 30;

    public int AttributionWindowDays { get; private set; }
    public QualifyingPaymentSource PaymentSource { get; private set; }
    public QualifyingEventRule QualifyingEvent { get; private set; }
    public long MinimumPaymentAmountCents { get; private set; }
    public string? MinimumPaymentCurrency { get; private set; }
    public int WaitingPeriodDays { get; private set; }
    public int MaximumRewardsPerReferrerPerCalendarYear { get; private set; }
    public ReferralRewardType RewardType { get; private set; }
    public string RewardDefinitionKey { get; private set; } = default!;
    public ReferralRefereeBenefitType? RefereeBenefitType { get; private set; }
    public int? RefereeBenefitPercentageBasisPoints { get; private set; }
    public long? RefereeBenefitFixedAmountCents { get; private set; }
    public string? RefereeBenefitCurrency { get; private set; }
    public int RefereeBenefitExpirationDays { get; private set; }

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
            RewardType = ReferralRewardType.SubscriptionFeatureGrant,
            RewardDefinitionKey = DefaultRewardDefinitionKey,
            RefereeBenefitType = null,
            RefereeBenefitPercentageBasisPoints = null,
            RefereeBenefitFixedAmountCents = null,
            RefereeBenefitCurrency = null,
            RefereeBenefitExpirationDays = DefaultRefereeBenefitExpirationDays,
        };

    public static Result<ReferralProgramPolicy> CreateTenantToTenant(
        int attributionWindowDays,
        long minimumPaymentAmountCents,
        string? minimumPaymentCurrency,
        int waitingPeriodDays,
        int maximumRewardsPerReferrerPerCalendarYear,
        ReferralRewardType rewardType,
        string rewardDefinitionKey,
        ReferralRefereeBenefitType? refereeBenefitType = null,
        int? refereeBenefitPercentageBasisPoints = null,
        long? refereeBenefitFixedAmountCents = null,
        string? refereeBenefitCurrency = null,
        int refereeBenefitExpirationDays = DefaultRefereeBenefitExpirationDays
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
            refereeBenefitType,
            refereeBenefitPercentageBasisPoints,
            refereeBenefitFixedAmountCents,
            refereeBenefitCurrency,
            refereeBenefitExpirationDays
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
                RefereeBenefitType = refereeBenefitType,
                RefereeBenefitPercentageBasisPoints = refereeBenefitPercentageBasisPoints,
                RefereeBenefitFixedAmountCents = refereeBenefitFixedAmountCents,
                RefereeBenefitCurrency = NormalizeCurrency(refereeBenefitCurrency),
                RefereeBenefitExpirationDays = refereeBenefitExpirationDays,
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
            refereeBenefitType: null,
            refereeBenefitPercentageBasisPoints: null,
            refereeBenefitFixedAmountCents: null,
            refereeBenefitCurrency: null,
            refereeBenefitExpirationDays: DefaultRefereeBenefitExpirationDays
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
                RefereeBenefitType = null,
                RefereeBenefitPercentageBasisPoints = null,
                RefereeBenefitFixedAmountCents = null,
                RefereeBenefitCurrency = null,
                RefereeBenefitExpirationDays = DefaultRefereeBenefitExpirationDays,
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

    private static Result Validate(
        int attributionWindowDays,
        long minimumPaymentAmountCents,
        string? minimumPaymentCurrency,
        int waitingPeriodDays,
        int maximumRewardsPerReferrerPerCalendarYear,
        ReferralRewardType rewardType,
        string rewardDefinitionKey,
        ReferralRefereeBenefitType? refereeBenefitType,
        int? refereeBenefitPercentageBasisPoints,
        long? refereeBenefitFixedAmountCents,
        string? refereeBenefitCurrency,
        int refereeBenefitExpirationDays
    )
    {
        if (attributionWindowDays is < 1 or > 730)
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidAttributionWindow",
                    "AttributionWindowDays must be between 1 and 730."
                )
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
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidRewardType",
                    "RewardType is not supported."
                )
            );
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

        if (refereeBenefitExpirationDays is < 1 or > 365)
        {
            return Result.Failure(
                new Error(
                    "ReferralPolicy.InvalidRefereeBenefitExpiration",
                    "RefereeBenefitExpirationDays must be between 1 and 365."
                )
            );
        }

        if (refereeBenefitType is not null)
        {
            if (refereeBenefitType is not (ReferralRefereeBenefitType.Percentage or ReferralRefereeBenefitType.FixedAmount))
            {
                return Result.Failure(
                    new Error(
                        "ReferralPolicy.InvalidRefereeBenefitType",
                        "RefereeBenefitType must be Percentage or FixedAmount."
                    )
                );
            }

            if (refereeBenefitType == ReferralRefereeBenefitType.Percentage)
            {
                if (refereeBenefitPercentageBasisPoints is not (>= 1 and <= 10_000))
                {
                    return Result.Failure(
                        new Error(
                            "ReferralPolicy.InvalidRefereeBenefitPercentage",
                            "RefereeBenefitPercentageBasisPoints must be between 1 and 10000 when RefereeBenefitType is Percentage."
                        )
                    );
                }
            }
            else
            {
                if (refereeBenefitFixedAmountCents is not (> 0))
                {
                    return Result.Failure(
                        new Error(
                            "ReferralPolicy.InvalidRefereeBenefitFixedAmount",
                            "RefereeBenefitFixedAmountCents must be greater than zero when RefereeBenefitType is FixedAmount."
                        )
                    );
                }

                if (
                    string.IsNullOrWhiteSpace(refereeBenefitCurrency)
                    || refereeBenefitCurrency.Trim().Length != 3
                )
                {
                    return Result.Failure(
                        new Error(
                            "ReferralPolicy.InvalidRefereeBenefitCurrency",
                            "RefereeBenefitCurrency is required and must be a 3-letter ISO code when RefereeBenefitType is FixedAmount."
                        )
                    );
                }
            }
        }

        return Result.Success();
    }

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
}
