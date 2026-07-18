using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Settings;

/// <summary>
/// Actualización parcial de <see cref="SubscriptionTenantSettings"/>. Cada campo nulo
/// significa "no tocar"; un valor presente reemplaza el actual tras pasar la validación
/// del value object correspondiente.
/// </summary>
public sealed record SubscriptionSettingsPatch(
    bool? AllowAutoRenewTenantSubscription = null,
    bool? AllowAutoRenewSeats = null,
    bool? AllowSeatSelfAssignment = null,
    bool? AllowAdminSeatAssignment = null,
    int? MaxSeatsAllowed = null,
    bool ClearMaxSeatsAllowed = false,
    int? MinSeatsRequired = null,
    int? DefaultSeatRenewalDays = null,
    GracePeriod? TenantSubscriptionGracePeriod = null,
    GracePeriod? SeatGracePeriod = null,
    bool? AllowSeatReassignment = null,
    int? SeatReassignmentCooldownDays = null,
    bool? AllowAddons = null,
    bool? AllowTrial = null,
    TrialDays? TrialDays = null,
    bool? SuspendTenantWhenBaseSubscriptionExpired = null,
    bool? SuspendUserWhenSeatExpired = null,
    IReadOnlyCollection<int>? NotifyBeforeRenewalDays = null,
    int? NotifyAfterFailedRenewalDays = null,
    AutoRenewCascadeMode? AutoRenewCascadeMode = null,
    bool? PauseSeatRenewalsWhenBaseSuspended = null
);
