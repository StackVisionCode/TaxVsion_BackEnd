namespace TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

/// <summary>
/// Paso de provisioning en curso mientras <see cref="TenantOnboarding.Status"/> es
/// <see cref="TenantOnboardingStatus.Provisioning"/> o <see cref="TenantOnboardingStatus.ProvisioningFailed"/>.
/// Ver PayFlow_Implementation_Plan.md §6.2.
/// </summary>
public enum TenantProvisioningStep
{
    None,
    Tenant,
    TenantAdmin,
    Subscription,
    CloudStorage,
    Subdomain,
    Defaults,
    Completed,
}
