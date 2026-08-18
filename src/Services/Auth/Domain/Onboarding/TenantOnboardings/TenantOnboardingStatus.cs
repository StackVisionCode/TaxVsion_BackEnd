namespace TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

/// <summary>
/// Estado persistido del proceso de onboarding pago-primero (PayFlow). Ver
/// PayFlow_Implementation_Plan.md §6.1 para las transiciones válidas de cada valor.
/// </summary>
public enum TenantOnboardingStatus
{
    PendingPayment,
    PaymentProcessing,
    PaymentCompleted,
    RegistrationPending,
    Provisioning,
    ProvisioningFailed,
    ManualReview,
    Completed,
    PaymentFailed,
    Cancelled,
    Expired,
    Refunded,
}
