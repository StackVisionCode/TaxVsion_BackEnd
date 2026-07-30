using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Admin.Commands;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Tests.Application;
using TaxVision.Auth.Tests.Onboarding;

namespace TaxVision.Auth.Tests.Onboarding.Admin;

/// <summary>PayFlow Fase 17 — comandos administrativos de OnboardingAdminController.</summary>
public sealed class OnboardingAdminCommandsTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static TenantOnboarding FailedAt(TenantProvisioningStep step, string code = "Tenant.RequestFailed")
    {
        var onboarding = TenantOnboarding
            .Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now)
            .Value;
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");
        onboarding.MarkPaymentCompleted("cs_test", Now);
        onboarding.SetRegistrationToken(
            TaxVision.Auth.Domain.Onboarding.ValueObjects.RegistrationTokenHash.Create(new string('a', 64)).Value,
            Now.AddHours(72)
        );
        onboarding.StartProvisioning(
            "Castillo Tax",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "ua",
            Now
        );

        if (
            step
            is TenantProvisioningStep.TenantAdmin
                or TenantProvisioningStep.Subscription
                or TenantProvisioningStep.CloudStorage
        )
            onboarding.SetTenantCreated(Guid.NewGuid());
        if (step is TenantProvisioningStep.Subscription or TenantProvisioningStep.CloudStorage)
            onboarding.SetTenantAdminCreated(Guid.NewGuid());
        if (step is TenantProvisioningStep.CloudStorage)
            onboarding.SetSubscriptionActivated(Guid.NewGuid());

        onboarding.MarkProvisioningFailed(step, code, "boom");
        return onboarding;
    }

    // ---------- ResumeOnboardingAdminCommand ----------

    [Fact]
    public async Task Resume_dispatches_saga_command_and_resets_retry_state()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant);
        onboarding.ScheduleRetry(Now.AddMinutes(5));
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };
        var bus = new FakeMessageBus();

        var result = await ResumeOnboardingAdminHandler.Handle(
            new ResumeOnboardingAdminCommand(onboarding.Id),
            repo,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, onboarding.RetryAttempt);
        Assert.Single(bus.Published);
        Assert.IsType<ResumeOnboardingProvisioningCommand>(bus.Published[0]);
    }

    [Fact]
    public async Task Resume_rejects_tenant_admin_failed_step()
    {
        var onboarding = FailedAt(TenantProvisioningStep.TenantAdmin, "Auth.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await ResumeOnboardingAdminHandler.Handle(
            new ResumeOnboardingAdminCommand(onboarding.Id),
            repo,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NotResumable", result.Error.Code);
    }

    [Fact]
    public async Task Resume_returns_not_found_when_missing()
    {
        var repo = new FakeTenantOnboardingRepository();

        var result = await ResumeOnboardingAdminHandler.Handle(
            new ResumeOnboardingAdminCommand(Guid.NewGuid()),
            repo,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NotFound", result.Error.Code);
    }

    // ---------- UpdateAndResumeOnboardingAdminCommand ----------

    [Fact]
    public async Task UpdateAndResume_updates_inputs_and_dispatches()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "Tenant.Subdomain");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };
        var bus = new FakeMessageBus();
        var newPlanId = Guid.NewGuid();

        var result = await UpdateAndResumeOnboardingAdminHandler.Handle(
            new UpdateAndResumeOnboardingAdminCommand(onboarding.Id, "newsubdomain", newPlanId),
            repo,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("newsubdomain", onboarding.RequestedSubdomain);
        Assert.Equal(newPlanId, onboarding.PlanId);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task UpdateAndResume_rejects_tenant_admin_failed_step()
    {
        var onboarding = FailedAt(TenantProvisioningStep.TenantAdmin, "Auth.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await UpdateAndResumeOnboardingAdminHandler.Handle(
            new UpdateAndResumeOnboardingAdminCommand(onboarding.Id, "newsubdomain", null),
            repo,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NotResumable", result.Error.Code);
    }

    // ---------- ForceCompleteOnboardingAdminCommand ----------

    [Fact]
    public async Task ForceComplete_succeeds_when_all_identities_exist()
    {
        var onboarding = FailedAt(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await ForceCompleteOnboardingAdminHandler.Handle(
            new ForceCompleteOnboardingAdminCommand(onboarding.Id, "Verified downstream resources exist manually."),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
    }

    [Fact]
    public async Task ForceComplete_rejects_blank_reason()
    {
        var onboarding = FailedAt(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await ForceCompleteOnboardingAdminHandler.Handle(
            new ForceCompleteOnboardingAdminCommand(onboarding.Id, "   "),
            repo,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ForceCompleteReasonRequired", result.Error.Code);
    }

    // ---------- CancelAndRefundOnboardingAdminCommand ----------

    [Fact]
    public async Task CancelAndRefund_publishes_refund_and_compensation_events()
    {
        var onboarding = FailedAt(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };
        var bus = new FakeMessageBus();

        var result = await CancelAndRefundOnboardingAdminHandler.Handle(
            new CancelAndRefundOnboardingAdminCommand(
                onboarding.Id,
                "Customer requested a refund after repeated failures.",
                "I understand this is irreversible"
            ),
            repo,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Refunded, onboarding.Status);
        Assert.Equal(2, bus.Published.Count);
        Assert.Contains(bus.Published, m => m is OnboardingRefundRequestedIntegrationEvent);
        Assert.Contains(bus.Published, m => m is OnboardingCancelRequestedIntegrationEvent);
    }

    [Fact]
    public async Task CancelAndRefund_rejects_wrong_confirmation_text()
    {
        var onboarding = FailedAt(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed");
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await CancelAndRefundOnboardingAdminHandler.Handle(
            new CancelAndRefundOnboardingAdminCommand(onboarding.Id, "reason", "sure, why not"),
            repo,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ConfirmationRequired", result.Error.Code);
    }

    [Fact]
    public async Task CancelAndRefund_rejects_when_no_payment_id()
    {
        var onboarding = TenantOnboarding
            .Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now)
            .Value;
        var repo = new FakeTenantOnboardingRepository { Existing = onboarding };

        var result = await CancelAndRefundOnboardingAdminHandler.Handle(
            new CancelAndRefundOnboardingAdminCommand(onboarding.Id, "reason", "I understand this is irreversible"),
            repo,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NoPayment", result.Error.Code);
    }
}
