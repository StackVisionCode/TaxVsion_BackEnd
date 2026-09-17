namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public sealed record MySubscriptionResponse(
    string PlanCode,
    string PlanName,
    string Status,
    string BillingCycle,
    decimal MonthlyPriceUsd,
    decimal CurrentCyclePriceUsd,
    int MaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules,
    DateTime? TrialEndsAtUtc,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? CancelledAtUtc,
    // Expiración/Dunning (Fase 4) — el frontend arma con esto el banner/gate por estado y sabe cuándo se
    // corta el acceso. BillingAccessBlocked se deriva localmente del estado (Suspended/Expired = bloqueado,
    // mismo criterio que TenantSubscriptionAccessConsumer en Auth) para no acoplar con el tenant de Auth.
    DateTime? NextRenewalAtUtc,
    DateTime? GracePeriodEndsAtUtc,
    DateTime? SuspendedAtUtc,
    DateTime? ExpiredAtUtc,
    string? SuspensionReason,
    LastPaymentFailureDto? LastPaymentFailure,
    bool BillingAccessBlocked
);

/// <summary>Último fallo de cobro de renovación (proyectado desde el <c>TenantSubscriptionRenewal</c> más
/// reciente con FailureCode) — para el copy "tu pago falló: {reason}" y el estado del reintento.</summary>
public sealed record LastPaymentFailureDto(
    string Code,
    string Reason,
    int RetryCount,
    DateTime? NextRetryAtUtc,
    DateTime? FailedAtUtc
);
