using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Settings;

/// <summary>
/// Políticas comerciales configurables por tenant (auto-renew, grace periods, límites de
/// seats, cascada de renovación). Una instancia por tenant. Toda actualización pasa por
/// <see cref="ApplyPatch"/>, que valida cada campo tocado antes de aplicarlo.
/// </summary>
public sealed class SubscriptionTenantSettings : TenantEntity
{
    private readonly List<int> _notifyBeforeRenewalDays = [7, 3, 1];

    public bool AllowAutoRenewTenantSubscription { get; private set; } = true;
    public bool AllowAutoRenewSeats { get; private set; } = true;
    public bool AllowSeatSelfAssignment { get; private set; }
    public bool AllowAdminSeatAssignment { get; private set; } = true;
    public int? MaxSeatsAllowed { get; private set; }
    public int MinSeatsRequired { get; private set; }
    public int DefaultSeatRenewalDays { get; private set; } = 30;
    public GracePeriod TenantSubscriptionGracePeriod { get; private set; } = null!;
    public GracePeriod SeatGracePeriod { get; private set; } = null!;
    public bool AllowSeatReassignment { get; private set; } = true;
    public int SeatReassignmentCooldownDays { get; private set; }
    public bool AllowAddons { get; private set; } = true;
    public bool AllowTrial { get; private set; } = true;
    public TrialDays TrialDays { get; private set; } = null!;
    public bool SuspendTenantWhenBaseSubscriptionExpired { get; private set; } = true;
    public bool SuspendUserWhenSeatExpired { get; private set; } = true;
    public int NotifyAfterFailedRenewalDays { get; private set; } = 1;
    public AutoRenewCascadeMode AutoRenewCascadeMode { get; private set; } = AutoRenewCascadeMode.None;
    public bool PauseSeatRenewalsWhenBaseSuspended { get; private set; } = true;
    public PlanChangeEffectiveMode PlanChangeEffective { get; private set; } = PlanChangeEffectiveMode.Immediate;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<int> NotifyBeforeRenewalDays => _notifyBeforeRenewalDays;

    private SubscriptionTenantSettings() { }

    public static Result<SubscriptionTenantSettings> Default(Guid tenantId, Guid actorUserId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SubscriptionTenantSettings>(new Error("SubscriptionSettings.InvalidTenant", "TenantId is required."));

        var gracePeriodResult = GracePeriod.Create(7);
        var trialDaysResult = TrialDays.Create(14);

        var settings = new SubscriptionTenantSettings
        {
            TenantSubscriptionGracePeriod = gracePeriodResult.Value,
            SeatGracePeriod = gracePeriodResult.Value,
            TrialDays = trialDaysResult.Value,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        settings.SetTenant(tenantId);
        return Result.Success(settings);
    }

    public Result ApplyPatch(SubscriptionSettingsPatch patch, Guid actorUserId, DateTime nowUtc)
    {
        var validation = ValidatePatch(patch);
        if (validation.IsFailure) return validation;

        Apply(patch);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private Result ValidatePatch(SubscriptionSettingsPatch patch)
    {
        var effectiveMinSeats = patch.MinSeatsRequired ?? MinSeatsRequired;
        if (effectiveMinSeats < 0)
            return Result.Failure(new Error("SubscriptionSettings.InvalidMinSeats", "MinSeatsRequired cannot be negative."));

        var effectiveMaxSeats = patch.ClearMaxSeatsAllowed ? null : patch.MaxSeatsAllowed ?? MaxSeatsAllowed;
        if (effectiveMaxSeats is not null && effectiveMaxSeats < effectiveMinSeats)
            return Result.Failure(new Error("SubscriptionSettings.InvalidMaxSeats", "MaxSeatsAllowed cannot be less than MinSeatsRequired."));

        if (patch.DefaultSeatRenewalDays is < 1 or > 366)
            return Result.Failure(new Error("SubscriptionSettings.InvalidSeatRenewalDays", "DefaultSeatRenewalDays must be between 1 and 366."));

        if (patch.SeatReassignmentCooldownDays is < 0 or > 90)
            return Result.Failure(new Error("SubscriptionSettings.InvalidCooldown", "SeatReassignmentCooldownDays must be between 0 and 90."));

        if (patch.NotifyAfterFailedRenewalDays is < 0 or > 30)
            return Result.Failure(new Error("SubscriptionSettings.InvalidNotifyAfterFailed", "NotifyAfterFailedRenewalDays must be between 0 and 30."));

        if (patch.NotifyBeforeRenewalDays is not null)
        {
            foreach (var day in patch.NotifyBeforeRenewalDays)
            {
                if (day is < 0 or > 90)
                    return Result.Failure(new Error("SubscriptionSettings.InvalidNotifyBefore", "Each NotifyBeforeRenewalDays entry must be between 0 and 90."));
            }
        }

        return Result.Success();
    }

    private void Apply(SubscriptionSettingsPatch patch)
    {
        if (patch.AllowAutoRenewTenantSubscription is { } allowAutoRenewTenant)
            AllowAutoRenewTenantSubscription = allowAutoRenewTenant;

        if (patch.AllowAutoRenewSeats is { } allowAutoRenewSeats)
            AllowAutoRenewSeats = allowAutoRenewSeats;

        if (patch.AllowSeatSelfAssignment is { } allowSelfAssignment)
            AllowSeatSelfAssignment = allowSelfAssignment;

        if (patch.AllowAdminSeatAssignment is { } allowAdminAssignment)
            AllowAdminSeatAssignment = allowAdminAssignment;

        if (patch.ClearMaxSeatsAllowed)
            MaxSeatsAllowed = null;
        else if (patch.MaxSeatsAllowed is { } maxSeats)
            MaxSeatsAllowed = maxSeats;

        if (patch.MinSeatsRequired is { } minSeats)
            MinSeatsRequired = minSeats;

        if (patch.DefaultSeatRenewalDays is { } seatRenewalDays)
            DefaultSeatRenewalDays = seatRenewalDays;

        if (patch.TenantSubscriptionGracePeriod is { } tenantGrace)
            TenantSubscriptionGracePeriod = tenantGrace;

        if (patch.SeatGracePeriod is { } seatGrace)
            SeatGracePeriod = seatGrace;

        if (patch.AllowSeatReassignment is { } allowReassignment)
            AllowSeatReassignment = allowReassignment;

        if (patch.SeatReassignmentCooldownDays is { } cooldownDays)
            SeatReassignmentCooldownDays = cooldownDays;

        if (patch.AllowAddons is { } allowAddons)
            AllowAddons = allowAddons;

        if (patch.AllowTrial is { } allowTrial)
            AllowTrial = allowTrial;

        if (patch.TrialDays is { } trialDays)
            TrialDays = trialDays;

        if (patch.SuspendTenantWhenBaseSubscriptionExpired is { } suspendTenant)
            SuspendTenantWhenBaseSubscriptionExpired = suspendTenant;

        if (patch.SuspendUserWhenSeatExpired is { } suspendUser)
            SuspendUserWhenSeatExpired = suspendUser;

        if (patch.NotifyBeforeRenewalDays is { } notifyBefore)
        {
            _notifyBeforeRenewalDays.Clear();
            _notifyBeforeRenewalDays.AddRange(notifyBefore);
        }

        if (patch.NotifyAfterFailedRenewalDays is { } notifyAfter)
            NotifyAfterFailedRenewalDays = notifyAfter;

        if (patch.AutoRenewCascadeMode is { } cascadeMode)
            AutoRenewCascadeMode = cascadeMode;

        if (patch.PauseSeatRenewalsWhenBaseSuspended is { } pauseSeats)
            PauseSeatRenewalsWhenBaseSuspended = pauseSeats;

        if (patch.PlanChangeEffective is { } planChangeEffective)
            PlanChangeEffective = planChangeEffective;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
