using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Domain.Common;

namespace TaxVision.Referrals.Domain.Programs;

public sealed class ReferralProgram : TenantEntity
{
    public string ProgramCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public ReferralProgramScope ScopeType { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public ReferralFlowType FlowType { get; private set; }
    public ReferralProgramStatus Status { get; private set; }
    public ReferralProgramPolicy Policy { get; private set; } = default!;
    public int PolicyVersion { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime? EndsAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralProgram() { }

    public static Result<ReferralProgram> Create(
        string programCode,
        string name,
        ReferralProgramScope scopeType,
        Guid? tenantScopeId,
        ReferralFlowType flowType,
        ReferralProgramPolicy policy,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var validation = ValidateCreation(
            programCode,
            name,
            scopeType,
            tenantScopeId,
            flowType,
            policy,
            startsAtUtc,
            endsAtUtc,
            idempotencyKey,
            payloadFingerprint
        );
        if (validation.IsFailure)
            return Result.Failure<ReferralProgram>(validation.Error);

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralProgram>(actor.Error);

        var program = new ReferralProgram
            {
                ProgramCode = programCode.Trim().ToUpperInvariant(),
                Name = name.Trim(),
                ScopeType = scopeType,
                TenantScopeId = tenantScopeId,
                FlowType = flowType,
                Status = ReferralProgramStatus.Draft,
                Policy = policy,
                PolicyVersion = 1,
                StartsAtUtc = startsAtUtc,
                EndsAtUtc = endsAtUtc,
                IdempotencyKey = idempotencyKey.Trim(),
                PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
            };
        program.SetTenant(tenantScopeId ?? PlatformTenant.Id);
        return Result.Success(program);
    }

    public Result Activate(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (FlowType == ReferralFlowType.TaxpayerToTaxpayer)
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.TaxpayerFlowDeferred",
                    "Taxpayer-to-taxpayer programs are modeled but cannot be activated for production."
                )
            );
        }

        if (Status is not (ReferralProgramStatus.Draft or ReferralProgramStatus.Suspended))
        {
            return Result.Failure(
                new Error("ReferralProgram.InvalidTransition", $"Cannot activate a program from {Status}.")
            );
        }

        if (EndsAtUtc is not null && EndsAtUtc <= nowUtc)
            return Result.Failure(new Error("ReferralProgram.AlreadyEnded", "An ended program cannot be activated."));

        Status = ReferralProgramStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Suspend(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralProgramStatus.Active)
        {
            return Result.Failure(
                new Error("ReferralProgram.InvalidTransition", $"Cannot suspend a program from {Status}.")
            );
        }

        Status = ReferralProgramStatus.Suspended;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Retire(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralProgramStatus.Retired)
            return Result.Success();

        Status = ReferralProgramStatus.Retired;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReplacePolicy(ReferralProgramPolicy policy, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralProgramStatus.Active)
        {
            return Result.Failure(
                new Error("ReferralProgram.ActivePolicyImmutable", "Suspend the program before replacing its policy.")
            );
        }

        if (Status == ReferralProgramStatus.Retired)
            return Result.Failure(new Error("ReferralProgram.Retired", "A retired program cannot be modified."));

        if (!PolicyMatchesFlow(FlowType, policy))
        {
            return Result.Failure(
                new Error("ReferralProgram.PolicyFlowMismatch", "The policy payment source does not match the flow.")
            );
        }

        Policy = policy;
        PolicyVersion++;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result EnsureAcceptingAttributions(DateTime nowUtc)
    {
        if (Status != ReferralProgramStatus.Active)
            return Result.Failure(new Error("ReferralProgram.NotActive", "The referral program is not active."));

        if (nowUtc < StartsAtUtc)
            return Result.Failure(new Error("ReferralProgram.NotStarted", "The referral program has not started."));

        if (EndsAtUtc is not null && nowUtc >= EndsAtUtc)
            return Result.Failure(new Error("ReferralProgram.Ended", "The referral program has ended."));

        return Result.Success();
    }

    public DateTime CalculateAttributionExpiry(DateTime attributedAtUtc)
    {
        var policyExpiry = attributedAtUtc.AddDays(Policy.AttributionWindowDays);
        return EndsAtUtc is not null && EndsAtUtc.Value < policyExpiry ? EndsAtUtc.Value : policyExpiry;
    }

    private static Result ValidateCreation(
        string programCode,
        string name,
        ReferralProgramScope scopeType,
        Guid? tenantScopeId,
        ReferralFlowType flowType,
        ReferralProgramPolicy policy,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        string idempotencyKey,
        string payloadFingerprint
    )
    {
        if (!Enum.IsDefined(scopeType))
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidScope",
                    "Referral program scope is not supported."
                )
            );
        }

        if (!Enum.IsDefined(flowType))
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidFlow",
                    "Referral program flow is not supported."
                )
            );
        }

        if (policy is null)
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidPolicy",
                    "Referral program policy is required."
                )
            );
        }

        if (
            string.IsNullOrWhiteSpace(programCode)
            || programCode.Trim().Length > 50
            || programCode.Trim().Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_')
        )
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidCode",
                    "ProgramCode is required, has a maximum of 50 characters, and accepts letters, numbers, '-' or '_'."
                )
            );
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(
                new Error("ReferralProgram.InvalidName", "Name is required and must be 200 characters or fewer.")
            );
        }

        if (
            (scopeType == ReferralProgramScope.Platform && tenantScopeId is not null)
            || (
                scopeType == ReferralProgramScope.Tenant
                && (tenantScopeId is null || tenantScopeId == Guid.Empty)
            )
        )
        {
            return Result.Failure(
                new Error("ReferralProgram.InvalidScope", "TenantScopeId must match the selected program scope.")
            );
        }

        if (
            (flowType == ReferralFlowType.TenantToTenant && scopeType != ReferralProgramScope.Platform)
            || (flowType == ReferralFlowType.TaxpayerToTaxpayer && scopeType != ReferralProgramScope.Tenant)
        )
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidFlowScope",
                    "Tenant-to-tenant is platform-scoped; taxpayer-to-taxpayer is tenant-scoped."
                )
            );
        }

        if (!PolicyMatchesFlow(flowType, policy))
        {
            return Result.Failure(
                new Error("ReferralProgram.PolicyFlowMismatch", "The policy payment source does not match the flow.")
            );
        }

        if (endsAtUtc is not null && endsAtUtc <= startsAtUtc)
        {
            return Result.Failure(
                new Error("ReferralProgram.InvalidPeriod", "EndsAtUtc must be after StartsAtUtc.")
            );
        }

        return ValidateIdempotency(idempotencyKey, payloadFingerprint);
    }

    private static bool PolicyMatchesFlow(ReferralFlowType flowType, ReferralProgramPolicy policy) =>
        (flowType == ReferralFlowType.TenantToTenant && policy.PaymentSource == QualifyingPaymentSource.PaymentApp)
        || (
            flowType == ReferralFlowType.TaxpayerToTaxpayer
            && policy.PaymentSource == QualifyingPaymentSource.PaymentClient
        );

    private static Result ValidateIdempotency(string idempotencyKey, string payloadFingerprint)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidIdempotencyKey",
                    "IdempotencyKey is required and must be 200 characters or fewer."
                )
            );
        }

        if (!DomainGuards.IsSha256Hex(payloadFingerprint))
        {
            return Result.Failure(
                new Error(
                    "ReferralProgram.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        return Result.Success();
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
