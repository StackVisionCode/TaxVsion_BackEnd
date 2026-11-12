using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Common;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Referrals.Domain.Rewards;

public sealed class ReferralRewardCase : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid AttributionId { get; private set; }
    public Guid QualificationId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public ReferralParticipantType BeneficiaryType { get; private set; }
    public Guid BeneficiaryId { get; private set; }
    public ReferralRewardType RewardType { get; private set; }
    public string RewardDefinitionKey { get; private set; } = default!;

    // Snapshot del cálculo del premio (WalletCredit): se congela al solicitarlo para que un cambio
    // posterior de la policy no altere un premio ya ganado. Cero/null para los tipos no monetarios.
    public long PricePaidCents { get; private set; }
    public int RewardBasisPoints { get; private set; }
    public long? RewardCapCents { get; private set; }
    public long RewardAmountCents { get; private set; }
    public string? RewardCurrency { get; private set; }
    public Guid GrantId { get; private set; }
    public ReferralRewardCaseStatus Status { get; private set; }
    public DateTime EligibleAtUtc { get; private set; }
    public string? MaterializedBenefitReference { get; private set; }
    public string? FailureCode { get; private set; }
    public string? StateReason { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedBy { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralRewardCase() { }

    public static Result<ReferralRewardCase> Request(
        ReferralProgram program,
        ReferralAttribution attribution,
        ReferralQualification qualification,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            qualification.ProgramId != program.Id
            || qualification.AttributionId != attribution.Id
            || qualification.Decision != ReferralQualificationDecision.Qualified
            || qualification.RewardEligibleAtUtc is null
        )
        {
            return Result.Failure<ReferralRewardCase>(
                new Error(
                    "ReferralReward.InvalidQualification",
                    "A reward requires a qualified decision for the same program and attribution."
                )
            );
        }

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralRewardCase>(actor.Error);

        if (attribution.Status != ReferralAttributionStatus.Qualified)
        {
            return Result.Failure<ReferralRewardCase>(
                new Error("ReferralReward.AttributionNotQualified", "The attribution must be qualified.")
            );
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            return Result.Failure<ReferralRewardCase>(
                new Error("ReferralReward.InvalidIdempotencyKey", "A valid idempotency key is required.")
            );

        if (!DomainGuards.IsSha256Hex(payloadFingerprint))
        {
            return Result.Failure<ReferralRewardCase>(
                new Error(
                    "ReferralReward.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        var isWalletCredit = program.Policy.RewardType == ReferralRewardType.WalletCredit;
        long rewardAmountCents = 0;
        if (isWalletCredit)
        {
            if (
                !string.Equals(
                    qualification.PaymentCurrency.Trim(),
                    program.Policy.RewardCurrency,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Result.Failure<ReferralRewardCase>(
                    new Error(
                        "ReferralReward.CurrencyMismatch",
                        "The qualifying payment currency does not match the program reward currency."
                    )
                );
            }

            rewardAmountCents = program.Policy.CalculateRewardCents(qualification.PaymentAmountCents);
            if (rewardAmountCents <= 0)
            {
                return Result.Failure<ReferralRewardCase>(
                    new Error("ReferralReward.NoRewardDue", "The qualifying payment yields no reward amount.")
                );
            }
        }

        var rewardCase = new ReferralRewardCase
        {
            PricePaidCents = isWalletCredit ? qualification.PaymentAmountCents : 0,
            RewardBasisPoints = isWalletCredit ? program.Policy.RewardBasisPoints : 0,
            RewardCapCents = isWalletCredit ? program.Policy.RewardCapCents : null,
            RewardAmountCents = rewardAmountCents,
            RewardCurrency = isWalletCredit ? program.Policy.RewardCurrency : null,
            ProgramId = program.Id,
            AttributionId = attribution.Id,
            QualificationId = qualification.Id,
            TenantScopeId = program.TenantScopeId,
            BeneficiaryType = attribution.ReferrerType,
            BeneficiaryId = attribution.ReferrerId,
            RewardType = program.Policy.RewardType,
            RewardDefinitionKey = program.Policy.RewardDefinitionKey,
            GrantId = Guid.NewGuid(),
            Status = ReferralRewardCaseStatus.Requested,
            EligibleAtUtc = qualification.RewardEligibleAtUtc.Value,
            IdempotencyKey = idempotencyKey.Trim(),
            PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        rewardCase.SetTenant(
            attribution.ReferrerType == ReferralParticipantType.Tenant ? attribution.ReferrerId : program.TenantId
        );
        return Result.Success(rewardCase);
    }

    public Result BeginGrant(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralRewardCaseStatus.Requested)
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot begin grant from {Status}."));

        if (nowUtc < EligibleAtUtc)
            return Result.Failure(
                new Error("ReferralReward.WaitingPeriod", "The reward waiting period has not elapsed.")
            );

        Status = ReferralRewardCaseStatus.PendingGrant;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Se volvió a pedir al Wallet la operación pendiente (crédito o reversa). Solo registra cuándo, para que el
    /// worker no reintente en cada ciclo mientras espera la respuesta del Wallet.
    /// </summary>
    public Result NoteWalletRetry(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status is not (ReferralRewardCaseStatus.PendingGrant or ReferralRewardCaseStatus.ClawbackPending))
            return Result.Failure(
                new Error("ReferralReward.InvalidTransition", $"Nothing is pending with the wallet in {Status}.")
            );

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ConfirmGranted(string materializedBenefitReference, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.Granted && MaterializedBenefitReference == materializedBenefitReference)
            return Result.Success();

        if (Status != ReferralRewardCaseStatus.PendingGrant)
            return Result.Failure(
                new Error("ReferralReward.InvalidTransition", $"Cannot confirm grant from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(materializedBenefitReference) || materializedBenefitReference.Length > 200)
        {
            return Result.Failure(
                new Error(
                    "ReferralReward.InvalidBenefitReference",
                    "A materialized benefit reference of 200 characters or fewer is required."
                )
            );
        }

        MaterializedBenefitReference = materializedBenefitReference.Trim();
        Status = ReferralRewardCaseStatus.Granted;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkVested(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.Vested)
            return Result.Success();

        if (Status != ReferralRewardCaseStatus.Granted)
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot vest from {Status}."));

        Status = ReferralRewardCaseStatus.Vested;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Monotónico frente a refund/chargeback: incluso durante PendingGrant el caso avanza
    /// a clawback y nunca vuelve silenciosamente a Requested.
    /// </summary>
    public Result RequestClawback(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.ClawbackPending)
            return Result.Success();

        if (
            Status
            is not (
                ReferralRewardCaseStatus.PendingGrant
                or ReferralRewardCaseStatus.Granted
                or ReferralRewardCaseStatus.Vested
            )
        )
        {
            return Result.Failure(
                new Error("ReferralReward.InvalidTransition", $"Cannot request clawback from {Status}.")
            );
        }

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralReward.InvalidReason", "A clawback reason is required."));

        Status = ReferralRewardCaseStatus.ClawbackPending;
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ConfirmReversed(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.Reversed)
            return Result.Success();

        if (Status != ReferralRewardCaseStatus.ClawbackPending)
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot reverse from {Status}."));

        Status = ReferralRewardCaseStatus.Reversed;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkFailed(string failureCode, string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status is not (ReferralRewardCaseStatus.Requested or ReferralRewardCaseStatus.PendingGrant))
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot fail from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("ReferralReward.InvalidFailureCode", "FailureCode is required."));

        Status = ReferralRewardCaseStatus.Failed;
        FailureCode = failureCode.Trim().Length > 100 ? failureCode.Trim()[..100] : failureCode.Trim();
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MoveToManualReview(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralRewardCaseStatus.ClawbackPending)
            return Result.Failure(
                new Error("ReferralReward.InvalidTransition", $"Cannot move to manual review from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralReward.InvalidReason", "A manual-review reason is required."));

        Status = ReferralRewardCaseStatus.ManualReview;
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// El PlatformAdmin retiene un premio aún NO acreditado (sospecha de abuso) hasta revisarlo. Solo desde
    /// <c>Requested</c>: con un crédito en curso (<c>PendingGrant</c>) o ya hecho el camino correcto es el clawback.
    /// </summary>
    public Result FlagForReview(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.ManualReview)
            return Result.Success();

        if (Status != ReferralRewardCaseStatus.Requested)
            return Result.Failure(
                new Error("ReferralReward.InvalidTransition", $"Cannot flag for review from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralReward.InvalidReason", "A review reason is required."));

        Status = ReferralRewardCaseStatus.ManualReview;
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// La revisión dio el visto bueno: el premio vuelve a <c>Requested</c> y el worker lo acredita cuando venza su
    /// espera. Solo si nada se acreditó (sin referencia materializada): un caso que llegó a revisión desde un clawback
    /// no se libera por aquí.
    /// </summary>
    public Result ReleaseFromReview(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralRewardCaseStatus.ManualReview || MaterializedBenefitReference is not null)
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot release from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralReward.InvalidReason", "A release reason is required."));

        Status = ReferralRewardCaseStatus.Requested;
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Cancel(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardCaseStatus.Cancelled)
            return Result.Success();

        // Un caso en revisión que ya tiene crédito materializado (llegó desde un clawback) se revierte, no se cancela.
        if (
            Status is not (ReferralRewardCaseStatus.Requested or ReferralRewardCaseStatus.ManualReview)
            || Status == ReferralRewardCaseStatus.ManualReview && MaterializedBenefitReference is not null
        )
            return Result.Failure(new Error("ReferralReward.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralReward.InvalidReason", "A cancellation reason is required."));

        Status = ReferralRewardCaseStatus.Cancelled;
        StateReason = TrimReason(reason);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static string TrimReason(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
