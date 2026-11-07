using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Application.Permissions.Admin.Commands;
using TaxVision.Auth.Application.Users.Commands;
using Wolverine.Runtime;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// AUTH-7 y BB-16: el host arranca aunque un handler no compile, porque Wolverine genera su código con el primer
/// mensaje. Compila aquí los que ahora reciben colaboradores, contra el contenedor real.
/// </summary>
public sealed class AuthHandlersCompileTests(AuthRoutingHostFactory factory) : IClassFixture<AuthRoutingHostFactory>
{
    public static TheoryData<Type> Messages =>
        [
            typeof(LoginCommand),
            typeof(OffboardUserCommand),
            typeof(DeactivateUserCommand),
            typeof(RestoreTenantAdminCommand),
            typeof(TenantSubscriptionStatusChangedIntegrationEvent),
            typeof(TenantStatusChangedIntegrationEvent),
            typeof(StartOnboardingCheckoutCommand),
            typeof(ReconcileOnboardingPaymentCommand),
            typeof(OnboardingPaymentSucceededIntegrationEvent),
        ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void Handler_BuildsInTheRealHost(Type message)
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();

        Assert.NotNull(runtime.Handlers.HandlerFor(message));
    }
}
