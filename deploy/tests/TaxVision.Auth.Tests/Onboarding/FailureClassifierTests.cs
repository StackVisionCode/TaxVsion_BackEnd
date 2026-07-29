using TaxVision.Auth.Application.Onboarding.Failures;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 17 — reglas de FailureClassifier.</summary>
public sealed class FailureClassifierTests
{
    [Theory]
    [InlineData("Auth.RequestFailed")]
    [InlineData("Auth.UnexpectedStatus")]
    [InlineData("Auth.EmptyResponse")]
    [InlineData("Anything.RequestFailed")]
    public void TenantAdmin_step_is_always_permanent_regardless_of_code(string failureCode)
    {
        var result = FailureClassifier.Classify(TenantProvisioningStep.TenantAdmin, failureCode);

        Assert.Equal(FailureKind.Permanent, result);
    }

    [Theory]
    [InlineData(TenantProvisioningStep.Tenant, "Tenant.RequestFailed")]
    [InlineData(TenantProvisioningStep.Tenant, "Tenant.UnexpectedStatus")]
    [InlineData(TenantProvisioningStep.Tenant, "Tenant.EmptyResponse")]
    [InlineData(TenantProvisioningStep.Subscription, "Subscription.RequestFailed")]
    [InlineData(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed")]
    public void Network_infra_codes_are_transient_for_non_tenant_admin_steps(
        TenantProvisioningStep step,
        string failureCode
    )
    {
        var result = FailureClassifier.Classify(step, failureCode);

        Assert.Equal(FailureKind.Transient, result);
    }

    [Theory]
    [InlineData(TenantProvisioningStep.Tenant, "Tenant.Subdomain")]
    [InlineData(TenantProvisioningStep.Subscription, "Subscription.Onboarding.PlanNotFound")]
    [InlineData(TenantProvisioningStep.CloudStorage, "CloudStorage.QuotaExceeded")]
    public void Domain_validation_codes_default_to_permanent(TenantProvisioningStep step, string failureCode)
    {
        var result = FailureClassifier.Classify(step, failureCode);

        Assert.Equal(FailureKind.Permanent, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_or_null_code_defaults_to_permanent(string? failureCode)
    {
        var result = FailureClassifier.Classify(TenantProvisioningStep.Tenant, failureCode!);

        Assert.Equal(FailureKind.Permanent, result);
    }
}
