using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Sagas;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 15 — orquestación de <see cref="TenantOnboardingProcessManager"/> y sus 6
/// comandos salientes. Los métodos <c>Handle</c> de la Saga se invocan directamente (mismo criterio
/// que el resto del repo: los tests unitarios llaman handlers sin pasar por el pipeline de
/// Wolverine) — no cubren el wiring runtime de Wolverine en sí (correlación por [SagaIdentity],
/// persistencia EF), que requiere el servicio real levantado.</summary>
public sealed class TenantOnboardingSagaTests
{
    private static readonly SecureTokenService Tokens = new();

    private static TenantOnboarding NewProvisioningOnboarding(DateTime now)
    {
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        Assert.True(onboarding.MarkPaymentProcessing(Guid.NewGuid(), "pi_123").IsSuccess);
        Assert.True(onboarding.MarkPaymentCompleted("pi_123", now).IsSuccess);

        var rawToken = Tokens.GenerateToken();
        var hash = RegistrationTokenHash.Create(Tokens.Hash(rawToken)).Value;
        Assert.True(onboarding.SetRegistrationToken(hash, now.AddHours(72)).IsSuccess);

        Assert.True(
            onboarding
                .StartProvisioning(
                    "Ada's Tax Office",
                    "adas-office",
                    Guid.NewGuid(),
                    new string('a', 64),
                    "127.0.0.1",
                    "xunit",
                    now
                )
                .IsSuccess
        );

        return onboarding;
    }

    private static OnboardingProvisioningStartedIntegrationEvent StartedEvent(TenantOnboarding onboarding) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = onboarding.Id,
            Email = onboarding.Email,
            FirstName = onboarding.FirstName,
            LastName = onboarding.LastName,
            PlanId = onboarding.PlanId,
            OfficeName = onboarding.OfficeName!,
            RequestedSubdomain = onboarding.RequestedSubdomain!,
            TermsVersionId = onboarding.TermsVersionId!.Value,
            PasswordHashReference = Guid.NewGuid(),
            PaymentCompletedAtUtc = onboarding.PaymentCompletedAtUtc!.Value,
        };

    [Fact]
    public void Start_creates_the_saga_and_cascades_CreateTenantForOnboardingCommand()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);

        var (saga, command) = TenantOnboardingProcessManager.Start(evt);

        Assert.Equal(evt.OnboardingId, saga.Id);
        Assert.Equal(evt.Email, saga.Email);
        Assert.Equal(evt.PasswordHashReference, saga.PasswordHashReference);
        Assert.Equal(evt.OnboardingId, command.OnboardingId);
        Assert.Equal(evt.OfficeName, command.OfficeName);
        Assert.Equal(evt.RequestedSubdomain, command.Subdomain);
        Assert.Equal(evt.PaymentCompletedAtUtc, command.PaymentCompletedAtUtc);
        Assert.False(saga.IsCompleted());
    }

    [Fact]
    public async Task Handle_TenantCreated_advances_the_aggregate_and_cascades_CreateTenantOwnerCommand()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);
        var (saga, _) = TenantOnboardingProcessManager.Start(evt);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();

        var tenantCreated = new TenantCreatedForOnboardingIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            OnboardingId = evt.OnboardingId,
            CreatedTenantId = Guid.NewGuid(),
        };

        var command = await saga.Handle(
            tenantCreated,
            onboardings,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.NotNull(command);
        Assert.Equal(tenantCreated.CreatedTenantId, saga.TenantId);
        Assert.Null(saga.PasswordHashReference);
        Assert.Equal(tenantCreated.CreatedTenantId, command!.TenantId);
        Assert.Equal(evt.Email, command.Email);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(TenantProvisioningStep.TenantAdmin, onboarding.CurrentStep);
        Assert.Equal(tenantCreated.CreatedTenantId, onboarding.TenantId);
    }

    [Fact]
    public async Task Handle_TenantOwnerCreated_advances_the_aggregate_and_cascades_ActivateSubscriptionCommand()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);
        var (saga, _) = TenantOnboardingProcessManager.Start(evt);
        var tenantId = Guid.NewGuid();
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        saga.TenantId = tenantId;

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();

        var ownerCreated = new TenantOwnerCreatedIntegrationEvent
        {
            TenantId = tenantId,
            OnboardingId = evt.OnboardingId,
            CreatedUserId = Guid.NewGuid(),
        };

        var command = await saga.Handle(
            ownerCreated,
            onboardings,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.NotNull(command);
        Assert.Equal(ownerCreated.CreatedUserId, saga.UserId);
        Assert.Equal(tenantId, command!.TenantId);
        Assert.Equal(saga.PlanId, command.PlanId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(TenantProvisioningStep.Subscription, onboarding.CurrentStep);
    }

    [Fact]
    public async Task Handle_SubscriptionActivated_advances_the_aggregate_and_cascades_ProvisionStorageForTenantCommand()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);
        var (saga, _) = TenantOnboardingProcessManager.Start(evt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        Assert.True(onboarding.SetTenantAdminCreated(userId).IsSuccess);
        saga.TenantId = tenantId;
        saga.UserId = userId;

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();

        var subscriptionActivated = new SubscriptionActivatedForOnboardingIntegrationEvent
        {
            TenantId = tenantId,
            OnboardingId = evt.OnboardingId,
            CreatedSubscriptionId = Guid.NewGuid(),
        };

        var command = await saga.Handle(
            subscriptionActivated,
            onboardings,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.NotNull(command);
        Assert.Equal(subscriptionActivated.CreatedSubscriptionId, saga.SubscriptionId);
        Assert.Equal(tenantId, command!.TenantId);
        Assert.Equal(userId, command.UserId);
        Assert.Equal(subscriptionActivated.CreatedSubscriptionId, command.SubscriptionId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(TenantProvisioningStep.CloudStorage, onboarding.CurrentStep);
    }

    [Fact]
    public async Task Handle_StepFailed_marks_the_aggregate_failed_and_leaves_the_saga_alive()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);
        var (saga, _) = TenantOnboardingProcessManager.Start(evt);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();

        var failed = new OnboardingProvisioningStepFailedIntegrationEvent
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = evt.OnboardingId,
            FailedStep = TenantProvisioningStep.Tenant.ToString(),
            FailureCode = "TenantProvisioningClient.RequestFailed",
            FailureReason = "Could not reach Tenant.",
        };

        await saga.Handle(
            failed,
            onboardings,
            unitOfWork,
            new FakeOnboardingMetrics(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(TenantOnboardingStatus.ProvisioningFailed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Tenant, onboarding.FailedStep);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.False(saga.IsCompleted());
    }

    [Fact]
    public void Handle_TenantOnboardingCompleted_marks_the_saga_completed()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var evt = StartedEvent(onboarding);
        var (saga, _) = TenantOnboardingProcessManager.Start(evt);

        saga.Handle(
            new TenantOnboardingCompletedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                OnboardingId = evt.OnboardingId,
                CompletedTenantId = Guid.NewGuid(),
                CompletedUserId = Guid.NewGuid(),
                CompletedSubscriptionId = Guid.NewGuid(),
            },
            new FakeCorrelationContext()
        );

        Assert.True(saga.IsCompleted());
    }

    [Fact]
    public async Task CreateTenantForOnboardingHandler_does_not_cascade_on_success()
    {
        var command = new CreateTenantForOnboardingCommand(
            Guid.NewGuid(),
            "Ada's Office",
            "adas-office",
            "buyer@example.com",
            DateTime.UtcNow
        );
        var client = new FakeTenantProvisioningClient(Result.Success());
        var correlation = new FakeCorrelationContext();

        var result = await CreateTenantForOnboardingHandler.Handle(
            command,
            client,
            correlation,
            CancellationToken.None
        );

        Assert.Null(result);
        Assert.Equal(command.OnboardingId, client.LastRequest!.OnboardingId);
    }

    [Fact]
    public async Task CreateTenantForOnboardingHandler_publishes_a_step_failed_event_on_failure()
    {
        var command = new CreateTenantForOnboardingCommand(
            Guid.NewGuid(),
            "Ada's Office",
            "adas-office",
            "buyer@example.com",
            DateTime.UtcNow
        );
        var client = new FakeTenantProvisioningClient(
            Result.Failure(new Error("TenantProvisioningClient.RequestFailed", "boom"))
        );
        var correlation = new FakeCorrelationContext();

        var result = await CreateTenantForOnboardingHandler.Handle(
            command,
            client,
            correlation,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(PlatformTenant.Id, result!.TenantId);
        Assert.Equal(command.OnboardingId, result.OnboardingId);
        Assert.Equal(TenantProvisioningStep.Tenant.ToString(), result.FailedStep);
        Assert.Equal("TenantProvisioningClient.RequestFailed", result.FailureCode);
    }

    [Fact]
    public async Task CreateTenantOwnerHandler_publishes_a_step_failed_event_on_failure()
    {
        var command = new CreateTenantOwnerCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "Ada",
            "Lovelace",
            Guid.NewGuid()
        );
        var client = new FakeAuthInternalOwnerCreationClient(
            Result.Failure(new Error("AuthInternalOwnerCreationClient.RequestFailed", "boom"))
        );
        var correlation = new FakeCorrelationContext();

        var result = await CreateTenantOwnerHandler.Handle(command, client, correlation, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TenantProvisioningStep.TenantAdmin.ToString(), result!.FailedStep);
    }

    // Auditoría F16 — CreateTenantOwnerHandler solo tenía cobertura del branch de fallo; el happy
    // path (fire-and-forget, sin cascada porque la Saga avanza vía TenantOwnerCreatedIntegrationEvent
    // del bus, no por el valor de retorno) nunca se había ejercitado.
    [Fact]
    public async Task CreateTenantOwnerHandler_does_not_cascade_on_success()
    {
        var command = new CreateTenantOwnerCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "Ada",
            "Lovelace",
            Guid.NewGuid()
        );
        var client = new FakeAuthInternalOwnerCreationClient(Result.Success());
        var correlation = new FakeCorrelationContext();

        var result = await CreateTenantOwnerHandler.Handle(command, client, correlation, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(command.OnboardingId, client.LastRequest!.OnboardingId);
        Assert.Equal(command.PasswordHashReference, client.LastRequest.PasswordHashReference);
    }

    [Fact]
    public async Task ActivateSubscriptionHandler_publishes_a_step_failed_event_on_failure()
    {
        var command = new ActivateSubscriptionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Monthly");
        var client = new FakeSubscriptionActivationClient(
            Result.Failure(new Error("SubscriptionActivationClient.RequestFailed", "boom"))
        );
        var correlation = new FakeCorrelationContext();

        var result = await ActivateSubscriptionHandler.Handle(command, client, correlation, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TenantProvisioningStep.Subscription.ToString(), result!.FailedStep);
    }

    // Auditoría F16 — mismo gap que CreateTenantOwnerHandler: solo el branch de fallo tenía test.
    [Fact]
    public async Task ActivateSubscriptionHandler_does_not_cascade_on_success()
    {
        var command = new ActivateSubscriptionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Monthly");
        var client = new FakeSubscriptionActivationClient(Result.Success());
        var correlation = new FakeCorrelationContext();

        var result = await ActivateSubscriptionHandler.Handle(command, client, correlation, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(command.OnboardingId, client.LastRequest!.OnboardingId);
        Assert.Equal(command.PlanId, client.LastRequest.PlanId);
    }

    [Fact]
    public async Task ProvisionStorageForTenantHandler_marks_the_step_and_cascades()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        Assert.True(onboarding.SetTenantAdminCreated(userId).IsSuccess);
        Assert.True(onboarding.SetSubscriptionActivated(subscriptionId).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ProvisionStorageForTenantCommand(onboarding.Id, tenantId, userId, subscriptionId);

        var next = await ProvisionStorageForTenantHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        Assert.NotNull(next);
        Assert.Equal(TenantProvisioningStep.Subdomain, onboarding.CurrentStep);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    // Auditoría F16 — ProvisionStorageForTenantHandler solo tenía el happy path cubierto; el branch
    // de fallo (Fase 17: publica OnboardingProvisioningStepFailedIntegrationEvent en vez de descartar
    // en silencio) nunca se había ejercitado. Onboarding recién arrancado (CurrentStep=Tenant) hace
    // que MarkStepCompleted(CloudStorage) falle por transición inválida.
    [Fact]
    public async Task ProvisionStorageForTenantHandler_publishes_a_step_failed_event_on_failure()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ProvisionStorageForTenantCommand(
            onboarding.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        );

        var result = await ProvisionStorageForTenantHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        var stepFailed = Assert.IsType<OnboardingProvisioningStepFailedIntegrationEvent>(result);
        Assert.Equal(TenantProvisioningStep.CloudStorage.ToString(), stepFailed.FailedStep);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ActivateSubdomainForTenantHandler_marks_the_step_and_cascades()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        Assert.True(onboarding.SetTenantAdminCreated(userId).IsSuccess);
        Assert.True(onboarding.SetSubscriptionActivated(subscriptionId).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ActivateSubdomainForTenantCommand(onboarding.Id, tenantId, userId, subscriptionId);

        var next = await ActivateSubdomainForTenantHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        Assert.NotNull(next);
        Assert.Equal(TenantProvisioningStep.Defaults, onboarding.CurrentStep);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    // Auditoría F16 — mismo gap que ProvisionStorageForTenantHandler: branch de fallo nunca cubierto.
    [Fact]
    public async Task ActivateSubdomainForTenantHandler_publishes_a_step_failed_event_on_failure()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ActivateSubdomainForTenantCommand(
            onboarding.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        );

        var result = await ActivateSubdomainForTenantHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        var stepFailed = Assert.IsType<OnboardingProvisioningStepFailedIntegrationEvent>(result);
        Assert.Equal(TenantProvisioningStep.Subdomain.ToString(), stepFailed.FailedStep);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ConfigureTenantDefaultsHandler_finalizes_the_onboarding_and_publishes_completed()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        Assert.True(onboarding.SetTenantAdminCreated(userId).IsSuccess);
        Assert.True(onboarding.SetSubscriptionActivated(subscriptionId).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ConfigureTenantDefaultsCommand(onboarding.Id, tenantId, userId, subscriptionId);

        var completedRaw = await ConfigureTenantDefaultsHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        var completed = Assert.IsType<TenantOnboardingCompletedIntegrationEvent>(completedRaw);
        Assert.Equal(tenantId, completed.TenantId);
        Assert.Equal(userId, completed.CompletedUserId);
        Assert.Equal(subscriptionId, completed.CompletedSubscriptionId);
        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
        Assert.NotNull(onboarding.RegistrationTokenUsedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    // Auditoría F16 — mismo gap que los otros 2 pasos locales: branch de fallo nunca cubierto.
    [Fact]
    public async Task ConfigureTenantDefaultsHandler_publishes_a_step_failed_event_on_failure()
    {
        var now = DateTime.UtcNow;
        var onboarding = NewProvisioningOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();
        var command = new ConfigureTenantDefaultsCommand(onboarding.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await ConfigureTenantDefaultsHandler.Handle(
            command,
            onboardings,
            unitOfWork,
            correlation,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        var stepFailed = Assert.IsType<OnboardingProvisioningStepFailedIntegrationEvent>(result);
        Assert.Equal(TenantProvisioningStep.Defaults.ToString(), stepFailed.FailedStep);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
