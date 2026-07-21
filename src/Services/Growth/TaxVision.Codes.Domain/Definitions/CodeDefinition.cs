using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Definitions;

public sealed class CodeDefinition : AggregateRoot
{
    private readonly List<CodeRuleVersion> _ruleVersions = [];
    private readonly List<CodeScope> _scopes = [];

    public CodeOwnerScope OwnerScope { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public string Name { get; private set; } = default!;
    public CodeKind Kind { get; private set; }
    public CodeTokenHash CodeHash { get; private set; } = null!;
    public CodeDisplay Display { get; private set; } = null!;
    public CodeDefinitionStatus Status { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public long? MaxRedemptions { get; private set; }
    public long? MaxRedemptionsPerTenant { get; private set; }
    public long? MaxRedemptionsPerSubject { get; private set; }
    public long ActiveReservations { get; private set; }
    public long CommittedRedemptions { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<CodeRuleVersion> RuleVersions => _ruleVersions;
    public IReadOnlyCollection<CodeScope> Scopes => _scopes;

    private CodeDefinition() { }

    public static Result<CodeDefinition> Create(
        Guid ownerTenantId,
        CodeOwnerScope ownerScope,
        Guid? tenantScopeId,
        string name,
        CodeKind kind,
        CodeTokenHash codeHash,
        CodeDisplay display,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc,
        long? maxRedemptions,
        long? maxRedemptionsPerTenant,
        long? maxRedemptionsPerSubject,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (ownerTenantId == Guid.Empty)
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidOwnerTenant", "Owner TenantId is required.")
            );

        if (!Enum.IsDefined(ownerScope))
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidOwnerScope", "Owner scope is invalid.")
            );

        if (ownerScope == CodeOwnerScope.Platform && ownerTenantId != PlatformTenant.Id)
            return Result.Failure<CodeDefinition>(
                new Error(
                    "Codes.CodeDefinition.InvalidPlatformOwner",
                    "A platform-owned code must belong to the canonical platform tenant."
                )
            );

        if (ownerScope == CodeOwnerScope.Tenant && ownerTenantId == PlatformTenant.Id)
            return Result.Failure<CodeDefinition>(
                new Error(
                    "Codes.CodeDefinition.InvalidTenantOwner",
                    "The platform tenant cannot own a tenant-scoped code."
                )
            );

        if (ownerScope == CodeOwnerScope.Tenant && tenantScopeId != ownerTenantId)
            return Result.Failure<CodeDefinition>(
                new Error(
                    "Codes.CodeDefinition.InvalidTenantScope",
                    "A tenant-owned code must be scoped to its owner tenant."
                )
            );

        if (tenantScopeId == Guid.Empty)
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidTenantScope", "TenantScopeId cannot be an empty GUID.")
            );

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidName", "Name is required and cannot exceed 200 characters.")
            );

        if (!Enum.IsDefined(kind))
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidKind", "Code kind is invalid.")
            );

        if (expiresAtUtc is not null && expiresAtUtc <= startsAtUtc)
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidValidity", "ExpiresAtUtc must be after StartsAtUtc.")
            );

        if (
            startsAtUtc.Kind != DateTimeKind.Utc
            || expiresAtUtc is not null && expiresAtUtc.Value.Kind != DateTimeKind.Utc
        )
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.NonUtcValidity", "Validity timestamps must be UTC.")
            );

        var limitsResult = ValidateLimits(maxRedemptions, maxRedemptionsPerTenant, maxRedemptionsPerSubject);
        if (limitsResult.IsFailure)
            return Result.Failure<CodeDefinition>(limitsResult.Error);

        if (actorUserId == Guid.Empty)
            return Result.Failure<CodeDefinition>(
                new Error("Codes.CodeDefinition.InvalidActor", "ActorUserId is required.")
            );

        var definition = new CodeDefinition
        {
            OwnerScope = ownerScope,
            TenantScopeId = tenantScopeId,
            Name = name.Trim(),
            Kind = kind,
            CodeHash = codeHash,
            Display = display,
            Status = CodeDefinitionStatus.Draft,
            StartsAtUtc = startsAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            MaxRedemptions = maxRedemptions,
            MaxRedemptionsPerTenant = maxRedemptionsPerTenant,
            MaxRedemptionsPerSubject = maxRedemptionsPerSubject,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        definition.SetTenant(ownerTenantId);
        return Result.Success(definition);
    }

    public Result<CodeRuleVersion> PublishRuleVersion(
        CodeBenefit benefit,
        Money? minimumPurchase,
        bool allowStacking,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status is CodeDefinitionStatus.Revoked or CodeDefinitionStatus.Expired)
            return Result.Failure<CodeRuleVersion>(
                new Error("Codes.CodeDefinition.Terminal", $"Cannot publish a rule while code is {Status}.")
            );

        var ruleResult = CodeRuleVersion.Create(
            TenantId,
            Id,
            _ruleVersions.Count + 1,
            benefit,
            minimumPurchase,
            allowStacking,
            actorUserId,
            nowUtc
        );
        if (ruleResult.IsFailure)
            return ruleResult;

        _ruleVersions.Add(ruleResult.Value);
        Touch(actorUserId, nowUtc);
        return ruleResult;
    }

    public Result<CodeScope> AddScope(
        CodeScopeType type,
        string scopeId,
        CodeScopeMode mode,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeDefinition.InvalidActor", "ActorUserId is required.")
            );

        if (Status is CodeDefinitionStatus.Revoked or CodeDefinitionStatus.Expired)
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeDefinition.Terminal", $"Cannot add a scope while code is {Status}.")
            );

        var scopeResult = CodeScope.Create(TenantId, Id, type, scopeId, mode);
        if (scopeResult.IsFailure)
            return scopeResult;

        if (
            _scopes.Any(scope =>
                scope.Type == type
                && scope.Mode == mode
                && string.Equals(scope.ScopeId, scopeResult.Value.ScopeId, StringComparison.Ordinal)
            )
        )
            return Result.Failure<CodeScope>(
                new Error("Codes.CodeDefinition.DuplicateScope", "An identical scope already exists.")
            );

        _scopes.Add(scopeResult.Value);
        Touch(actorUserId, nowUtc);
        return scopeResult;
    }

    public Result Activate(Guid actorUserId, DateTime nowUtc)
    {
        var actorResult = ValidateActor(actorUserId);
        if (actorResult.IsFailure)
            return actorResult;

        if (Status != CodeDefinitionStatus.Draft)
            return InvalidTransition(nameof(Activate));

        if (_ruleVersions.Count == 0)
            return Result.Failure(
                new Error("Codes.CodeDefinition.RuleRequired", "At least one published rule is required.")
            );

        if (ExpiresAtUtc is not null && nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("Codes.CodeDefinition.Expired", "An expired code cannot be activated."));

        Status = CodeDefinitionStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Suspend(Guid actorUserId, DateTime nowUtc)
    {
        var actorResult = ValidateActor(actorUserId);
        if (actorResult.IsFailure)
            return actorResult;

        if (Status != CodeDefinitionStatus.Active)
            return InvalidTransition(nameof(Suspend));

        Status = CodeDefinitionStatus.Suspended;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Reactivate(Guid actorUserId, DateTime nowUtc)
    {
        var actorResult = ValidateActor(actorUserId);
        if (actorResult.IsFailure)
            return actorResult;

        if (Status != CodeDefinitionStatus.Suspended)
            return InvalidTransition(nameof(Reactivate));

        if (ExpiresAtUtc is not null && nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("Codes.CodeDefinition.Expired", "An expired code cannot be reactivated."));

        Status = CodeDefinitionStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Revoke(Guid actorUserId, DateTime nowUtc)
    {
        var actorResult = ValidateActor(actorUserId);
        if (actorResult.IsFailure)
            return actorResult;

        if (Status is CodeDefinitionStatus.Revoked or CodeDefinitionStatus.Expired)
            return InvalidTransition(nameof(Revoke));

        Status = CodeDefinitionStatus.Revoked;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Expire(DateTime nowUtc)
    {
        if (Status is CodeDefinitionStatus.Revoked or CodeDefinitionStatus.Expired)
            return InvalidTransition(nameof(Expire));

        if (ExpiresAtUtc is null || nowUtc < ExpiresAtUtc)
            return Result.Failure(
                new Error("Codes.CodeDefinition.NotDueForExpiry", "The code has not reached its expiration time.")
            );

        Status = CodeDefinitionStatus.Expired;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result<CodeQuote> CreateQuote(
        Guid consumingTenantId,
        SubjectReference subject,
        OfferReference offer,
        IReadOnlyCollection<CodeScopeTarget> targets,
        Money grossAmount,
        SnapshotHash snapshotHash,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        TimeSpan timeToLive,
        DateTime nowUtc
    )
    {
        var eligibilityResult = EnsureCanQuote(consumingTenantId, targets, nowUtc);
        if (eligibilityResult.IsFailure)
            return Result.Failure<CodeQuote>(eligibilityResult.Error);

        if (timeToLive <= TimeSpan.Zero)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.InvalidTtl", "Quote time-to-live must be greater than zero.")
            );

        var expiresAtUtc = nowUtc.Add(timeToLive);
        if (ExpiresAtUtc is not null && expiresAtUtc > ExpiresAtUtc)
            expiresAtUtc = ExpiresAtUtc.Value;

        if (expiresAtUtc <= nowUtc)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.InvalidExpiry", "Quote expiration must be after creation.")
            );

        var rule = _ruleVersions.OrderByDescending(item => item.Version).First();
        var discountResult = rule.EvaluateDiscount(grossAmount);
        if (discountResult.IsFailure)
            return Result.Failure<CodeQuote>(discountResult.Error);

        var netResult = grossAmount.Subtract(discountResult.Value);
        if (netResult.IsFailure)
            return Result.Failure<CodeQuote>(netResult.Error);

        return CodeQuote.Create(
            consumingTenantId,
            Id,
            rule.Id,
            rule.Version,
            Display,
            subject,
            offer,
            grossAmount,
            discountResult.Value,
            netResult.Value,
            snapshotHash,
            idempotencyKey,
            payloadFingerprint,
            nowUtc,
            expiresAtUtc
        );
    }

    public Result ReserveUse(DateTime nowUtc)
    {
        if (
            Status != CodeDefinitionStatus.Active
            || nowUtc < StartsAtUtc
            || ExpiresAtUtc is not null && nowUtc >= ExpiresAtUtc
        )
            return Result.Failure(
                new Error("Codes.CodeDefinition.NotReservable", "Code is not active within its validity window.")
            );

        if (
            MaxRedemptions is { } maxRedemptions
            && (CommittedRedemptions >= maxRedemptions || ActiveReservations >= maxRedemptions - CommittedRedemptions)
        )
            return Result.Failure(
                new Error("Codes.CodeDefinition.NoAvailability", "The code has no remaining global availability.")
            );

        try
        {
            ActiveReservations = checked(ActiveReservations + 1);
        }
        catch (OverflowException)
        {
            return Result.Failure(
                new Error("Codes.CodeDefinition.CounterOverflow", "The reservation counter overflowed.")
            );
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result CommitReservedUse(DateTime nowUtc)
    {
        if (ActiveReservations <= 0)
            return Result.Failure(
                new Error("Codes.CodeDefinition.NoActiveReservation", "No active reservation is available to commit.")
            );

        try
        {
            ActiveReservations--;
            CommittedRedemptions = checked(CommittedRedemptions + 1);
        }
        catch (OverflowException)
        {
            ActiveReservations++;
            return Result.Failure(
                new Error("Codes.CodeDefinition.CounterOverflow", "The redemption counter overflowed.")
            );
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result CommitLateUse(DateTime nowUtc)
    {
        try
        {
            CommittedRedemptions = checked(CommittedRedemptions + 1);
        }
        catch (OverflowException)
        {
            return Result.Failure(
                new Error("Codes.CodeDefinition.CounterOverflow", "The redemption counter overflowed.")
            );
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result ReleaseReservedUse(DateTime nowUtc)
    {
        if (ActiveReservations <= 0)
            return Result.Failure(
                new Error("Codes.CodeDefinition.NoActiveReservation", "No active reservation is available to release.")
            );

        ActiveReservations--;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result RestoreCommittedUse(DateTime nowUtc)
    {
        if (CommittedRedemptions <= 0)
            return Result.Failure(
                new Error(
                    "Codes.CodeDefinition.NoCommittedRedemption",
                    "No committed redemption is available to restore."
                )
            );

        CommittedRedemptions--;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private Result EnsureCanQuote(Guid consumingTenantId, IReadOnlyCollection<CodeScopeTarget> targets, DateTime nowUtc)
    {
        if (consumingTenantId == Guid.Empty)
            return Result.Failure(new Error("Codes.CodeDefinition.InvalidTenant", "Consuming TenantId is required."));

        if (TenantScopeId is not null && TenantScopeId != consumingTenantId)
            return Result.Failure(
                new Error("Codes.CodeDefinition.TenantScopeMismatch", "Code is not available to this tenant.")
            );

        if (Status != CodeDefinitionStatus.Active)
            return Result.Failure(
                new Error("Codes.CodeDefinition.NotActive", $"Code is not active; current status is {Status}.")
            );

        if (nowUtc < StartsAtUtc)
            return Result.Failure(new Error("Codes.CodeDefinition.NotStarted", "Code validity has not started."));

        if (ExpiresAtUtc is not null && nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("Codes.CodeDefinition.Expired", "Code has expired."));

        if (_ruleVersions.Count == 0)
            return Result.Failure(
                new Error("Codes.CodeDefinition.RuleRequired", "Code does not have a published rule.")
            );

        if (_scopes.Where(scope => scope.Mode == CodeScopeMode.Exclude).Any(scope => targets.Any(scope.Matches)))
            return Result.Failure(
                new Error("Codes.CodeDefinition.ScopeExcluded", "The offer or subject is explicitly excluded.")
            );

        var includedScopes = _scopes.Where(scope => scope.Mode == CodeScopeMode.Include).ToList();
        if (includedScopes.Count > 0 && !includedScopes.Any(scope => targets.Any(scope.Matches)))
            return Result.Failure(
                new Error("Codes.CodeDefinition.ScopeNotIncluded", "The code does not include the requested scope.")
            );

        return Result.Success();
    }

    private static Result ValidateLimits(long? global, long? perTenant, long? perSubject)
    {
        if (global is <= 0 || perTenant is <= 0 || perSubject is <= 0)
            return Result.Failure(
                new Error("Codes.CodeDefinition.InvalidLimit", "Redemption limits must be greater than zero.")
            );

        if (global is not null && perTenant > global)
            return Result.Failure(
                new Error("Codes.CodeDefinition.InvalidTenantLimit", "Per-tenant limit cannot exceed global limit.")
            );

        if (global is not null && perSubject > global)
            return Result.Failure(
                new Error("Codes.CodeDefinition.InvalidSubjectLimit", "Per-subject limit cannot exceed global limit.")
            );

        return Result.Success();
    }

    private Result InvalidTransition(string operation) =>
        Result.Failure(
            new Error("Codes.CodeDefinition.InvalidTransition", $"Cannot {operation} a code from status {Status}.")
        );

    private static Result ValidateActor(Guid actorUserId) =>
        actorUserId == Guid.Empty
            ? Result.Failure(new Error("Codes.CodeDefinition.InvalidActor", "ActorUserId is required."))
            : Result.Success();

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
