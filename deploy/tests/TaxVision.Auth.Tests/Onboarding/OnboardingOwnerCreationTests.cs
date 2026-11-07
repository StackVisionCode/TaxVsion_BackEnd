using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Onboarding.Application.Sagas.Commands;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>AUTH-8: la Saga crea al dueño en proceso, sin el salto HTTP a sí mismo.</summary>
public sealed class OnboardingOwnerCreationTests
{
    [Fact]
    public async Task DomainFailure_BecomesAFailedStep()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Failure(new Error("Onboarding.PasswordReferenceExpired", "expired")),
        };

        var failed = await HandleAsync(Command(), bus);

        Assert.Equal(TenantProvisioningStep.TenantAdmin.ToString(), failed!.FailedStep);
        Assert.Equal("Onboarding.PasswordReferenceExpired", failed.FailureCode);
    }

    // Con HTTP una excepción del receptor llegaba como 500 y el paso fallaba: no puede ir a parar a la DLQ.
    [Fact]
    public async Task UnexpectedException_BecomesAFailedStepAndHidesTheDetail()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => throw new InvalidOperationException("sql detail") };

        var failed = await HandleAsync(Command(), bus);

        Assert.Equal(TenantProvisioningStep.TenantAdmin.ToString(), failed!.FailedStep);
        Assert.Equal("Onboarding.OwnerCreationFailed", failed.FailureCode);
        Assert.DoesNotContain("sql detail", failed.FailureReason);
    }

    [Fact]
    public async Task Cancellation_IsNotReportedAsAFailedStep()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => throw new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => HandleAsync(Command(), bus));
    }

    private static CreateTenantOwnerCommand Command() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.com", "Ada", "Lovelace", Guid.NewGuid());

    private static Task<OnboardingProvisioningStepFailedIntegrationEvent?> HandleAsync(
        CreateTenantOwnerCommand command,
        FakeMessageBus bus
    ) =>
        CreateTenantOwnerHandler.Handle(
            command,
            bus,
            new FakeCorrelationContext(),
            NullLogger<CreateTenantOwnerCommand>.Instance,
            CancellationToken.None
        );
}
