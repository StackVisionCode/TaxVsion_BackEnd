using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Application.Onboarding.Sagas.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingRetryProcessorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task ProcessDueAsync_schedules_first_transient_failure_without_dispatching_resume()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "TenantProvisioningClient.RequestFailed");
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(onboarding, unitOfWork, bus);

        var summary = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal((0, 0, 1), summary);
        Assert.Equal(0, onboarding.RetryAttempt);
        Assert.True(onboarding.NextRetryAtUtc > Now);
        Assert.Empty(bus.Published);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProcessDueAsync_dispatches_due_retry_without_clearing_failure_context()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "TenantProvisioningClient.RequestFailed");
        onboarding.ScheduleRetry(Now.AddMinutes(-1));
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(onboarding, unitOfWork, bus);

        var summary = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal((1, 0, 1), summary);
        Assert.Equal(TenantOnboardingStatus.ProvisioningFailed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Tenant, onboarding.FailedStep);
        Assert.Equal("TenantProvisioningClient.RequestFailed", onboarding.FailureCode);
        Assert.Equal(1, onboarding.RetryAttempt);
        Assert.True(onboarding.NextRetryAtUtc > Now);
        var command = Assert.Single(bus.Published);
        Assert.IsType<ResumeOnboardingProvisioningCommand>(command);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProcessDueAsync_exhausts_lost_retry_dispatches_instead_of_dispatching_forever()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "TenantProvisioningClient.RequestFailed");
        onboarding.ScheduleRetry(Now.AddMinutes(-1));
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(onboarding, unitOfWork, bus);

        await processor.ProcessDueAsync(Now, CancellationToken.None);
        Assert.Equal(1, onboarding.RetryAttempt);

        await processor.ProcessDueAsync(Now.AddMinutes(6), CancellationToken.None);
        Assert.Equal(2, onboarding.RetryAttempt);

        await processor.ProcessDueAsync(Now.AddMinutes(12), CancellationToken.None);
        Assert.Equal(3, onboarding.RetryAttempt);

        var summary = await processor.ProcessDueAsync(Now.AddMinutes(18), CancellationToken.None);

        Assert.Equal((0, 1, 1), summary);
        Assert.Equal(TenantOnboardingStatus.ManualReview, onboarding.Status);
        Assert.Equal(3, bus.Published.OfType<ResumeOnboardingProvisioningCommand>().Count());
    }

    [Fact]
    public async Task ProcessDueAsync_sends_non_retryable_failure_to_manual_review()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "Tenant.SubdomainTaken");
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(onboarding, unitOfWork, bus);

        var summary = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal((0, 1, 1), summary);
        Assert.Equal(TenantOnboardingStatus.ManualReview, onboarding.Status);
        Assert.Null(onboarding.NextRetryAtUtc);
        Assert.Empty(bus.Published);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProcessDueAsync_sends_exhausted_transient_failure_to_manual_review()
    {
        var onboarding = FailedAt(TenantProvisioningStep.Tenant, "TenantProvisioningClient.RequestFailed");
        RecordFailedAttempt(onboarding, Now.AddMinutes(-30));
        RecordFailedAttempt(onboarding, Now.AddMinutes(-20));
        RecordFailedAttempt(onboarding, Now.AddMinutes(-1));
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var processor = ProcessorFor(onboarding, unitOfWork, bus);

        var summary = await processor.ProcessDueAsync(Now, CancellationToken.None);

        Assert.Equal((0, 1, 1), summary);
        Assert.Equal(3, onboarding.RetryAttempt);
        Assert.Equal(TenantOnboardingStatus.ManualReview, onboarding.Status);
        Assert.Empty(bus.Published);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    private static OnboardingRetryProcessor ProcessorFor(
        TenantOnboarding onboarding,
        FakeUnitOfWork unitOfWork,
        FakeMessageBus bus
    ) =>
        new(new FakeTenantOnboardingRepository { Existing = onboarding }, unitOfWork, bus, new FakeOnboardingMetrics());

    private static TenantOnboarding FailedAt(TenantProvisioningStep step, string code)
    {
        var onboarding = TenantOnboarding
            .Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now)
            .Value;
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");
        onboarding.MarkPaymentCompleted("cs_test", Now);
        onboarding.SetRegistrationToken(RegistrationTokenHash.Create(new string('a', 64)).Value, Now.AddHours(72));
        onboarding.StartProvisioning(
            "Castillo Tax",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "xunit",
            Now
        );

        if (step == TenantProvisioningStep.Subscription)
        {
            onboarding.SetTenantCreated(Guid.NewGuid());
            onboarding.SetTenantAdminCreated(Guid.NewGuid());
        }

        onboarding.MarkProvisioningFailed(step, code, "boom");
        return onboarding;
    }

    private static void RecordFailedAttempt(TenantOnboarding onboarding, DateTime dueAtUtc)
    {
        onboarding.ScheduleRetry(dueAtUtc);
        onboarding.MarkRetryDispatched(dueAtUtc.AddMinutes(5));
        onboarding.ResumeProvisioning();
        onboarding.MarkProvisioningFailed(
            TenantProvisioningStep.Tenant,
            "TenantProvisioningClient.RequestFailed",
            "boom"
        );
    }
}
