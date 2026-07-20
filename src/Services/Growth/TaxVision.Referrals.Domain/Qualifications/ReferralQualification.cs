using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Common;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Domain.Qualifications;

/// <summary>
/// Decisión inmutable e idempotente por evento financiero. La unicidad efectiva
/// (AttributionId, QualifyingEventId) se refuerza posteriormente en persistencia.
/// </summary>
public sealed class ReferralQualification : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid AttributionId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public Guid QualifyingEventId { get; private set; }
    public Guid PaymentId { get; private set; }
    public QualifyingPaymentSource PaymentSource { get; private set; }
    public long PaymentAmountCents { get; private set; }
    public string PaymentCurrency { get; private set; } = default!;
    public bool IsFirstSuccessfulPayment { get; private set; }
    public ReferralQualificationDecision Decision { get; private set; }
    public string? RejectionReasonCode { get; private set; }
    public DateTime PaymentSucceededAtUtc { get; private set; }
    public DateTime? RewardEligibleAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime EvaluatedAtUtc { get; private set; }
    public Guid EvaluatedBy { get; private set; }

    private ReferralQualification() { }

    public static Result<ReferralQualification> Evaluate(
        ReferralProgram program,
        ReferralAttribution attribution,
        Guid qualifyingEventId,
        Guid paymentId,
        QualifyingPaymentSource paymentSource,
        long paymentAmountCents,
        string paymentCurrency,
        bool isFirstSuccessfulPayment,
        bool annualRewardSlotAvailable,
        DateTime paymentSucceededAtUtc,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (program.Id != attribution.ProgramId)
        {
            return Result.Failure<ReferralQualification>(
                new Error("ReferralQualification.ProgramMismatch", "Attribution does not belong to the program.")
            );
        }

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralQualification>(actor.Error);

        if (qualifyingEventId == Guid.Empty || paymentId == Guid.Empty)
        {
            return Result.Failure<ReferralQualification>(
                new Error(
                    "ReferralQualification.InvalidPaymentReference",
                    "QualifyingEventId and PaymentId are required."
                )
            );
        }

        if (paymentAmountCents <= 0)
        {
            return Result.Failure<ReferralQualification>(
                new Error("ReferralQualification.InvalidAmount", "A successful payment must be greater than zero.")
            );
        }

        if (string.IsNullOrWhiteSpace(paymentCurrency) || paymentCurrency.Trim().Length != 3)
        {
            return Result.Failure<ReferralQualification>(
                new Error("ReferralQualification.InvalidCurrency", "Payment currency must be a 3-letter ISO code.")
            );
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
        {
            return Result.Failure<ReferralQualification>(
                new Error("ReferralQualification.InvalidIdempotencyKey", "A valid idempotency key is required.")
            );
        }

        if (!DomainGuards.IsSha256Hex(payloadFingerprint))
        {
            return Result.Failure<ReferralQualification>(
                new Error(
                    "ReferralQualification.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        var rejection = DetermineRejection(
            program,
            attribution,
            paymentSource,
            paymentAmountCents,
            paymentCurrency,
            isFirstSuccessfulPayment,
            annualRewardSlotAvailable,
            paymentSucceededAtUtc
        );
        var qualified = rejection is null;

        var qualification = new ReferralQualification
            {
                ProgramId = program.Id,
                AttributionId = attribution.Id,
                TenantScopeId = program.TenantScopeId,
                QualifyingEventId = qualifyingEventId,
                PaymentId = paymentId,
                PaymentSource = paymentSource,
                PaymentAmountCents = paymentAmountCents,
                PaymentCurrency = paymentCurrency.Trim().ToUpperInvariant(),
                IsFirstSuccessfulPayment = isFirstSuccessfulPayment,
                Decision = qualified
                    ? ReferralQualificationDecision.Qualified
                    : ReferralQualificationDecision.Rejected,
                RejectionReasonCode = rejection,
                PaymentSucceededAtUtc = paymentSucceededAtUtc,
                RewardEligibleAtUtc = qualified
                    ? paymentSucceededAtUtc.AddDays(program.Policy.WaitingPeriodDays)
                    : null,
                IdempotencyKey = idempotencyKey.Trim(),
                PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
                EvaluatedAtUtc = nowUtc,
                EvaluatedBy = actorUserId,
            };
        qualification.SetTenant(attribution.TenantId);
        return Result.Success(qualification);
    }

    private static string? DetermineRejection(
        ReferralProgram program,
        ReferralAttribution attribution,
        QualifyingPaymentSource paymentSource,
        long paymentAmountCents,
        string paymentCurrency,
        bool isFirstSuccessfulPayment,
        bool annualRewardSlotAvailable,
        DateTime paymentSucceededAtUtc
    )
    {
        if (attribution.Status != ReferralAttributionStatus.Active)
            return "AttributionNotActive";

        if (paymentSucceededAtUtc < attribution.AttributedAtUtc)
            return "PaymentPredatesAttribution";

        if (paymentSucceededAtUtc >= attribution.ExpiresAtUtc)
            return "AttributionExpired";

        if (paymentSource != program.Policy.PaymentSource)
            return "WrongPaymentSource";

        if (
            program.Policy.QualifyingEvent == QualifyingEventRule.FirstSuccessfulPayment
            && !isFirstSuccessfulPayment
        )
            return "NotFirstSuccessfulPayment";

        if (!annualRewardSlotAvailable)
            return "AnnualRewardLimitReached";

        return program.Policy.MeetsMinimum(paymentAmountCents, paymentCurrency)
            ? null
            : "MinimumPaymentNotMet";
    }
}
