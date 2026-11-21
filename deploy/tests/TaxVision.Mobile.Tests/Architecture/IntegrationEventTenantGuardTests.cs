extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Web.Tenancy;
using Wolverine;

namespace TaxVision.Mobile.Tests.Architecture;

/// <summary>Un evento de integración sin oficina se rechaza antes de llegar a su consumer.</summary>
public sealed class IntegrationEventTenantGuardTests
{
    private const string ApiProject = "src/Services/Mobile/TaxVision.Mobile.Api";

    // En producción (Static) corren estos archivos, y la prueba de frescura garantiza que están al día.
    [Fact]
    public void Every_integration_event_handler_runs_the_tenant_guard()
    {
        var handlers = Directory.GetFiles(
            Path.Combine(PreGeneratedCodeRules.GeneratedDirectory(ApiProject), "WolverineHandlers"),
            "*IntegrationEventHandler*.cs"
        );

        Assert.NotEmpty(handlers);
        var unguarded = handlers
            .Where(file =>
                !File.ReadAllText(file).Contains("IntegrationEventTenantMiddleware.Before(", StringComparison.Ordinal)
            )
            .Select(Path.GetFileName);
        Assert.Empty(unguarded);
    }

    [Fact]
    public void An_event_with_an_empty_tenant_is_rejected_and_sets_no_tenant()
    {
        var evt = new UserDeactivatedIntegrationEvent
        {
            TenantId = Guid.Empty,
            UserId = Guid.NewGuid(),
            Email = "user@example.test",
            ActorType = "TenantEmployee",
        };
        var tenant = new TenantContext();
        var bus = DispatchProxy.Create<IMessageBus, UnusedBus>();

        Assert.Throws<InvalidOperationException>(() => IntegrationEventTenantMiddleware.Before(evt, tenant, bus));
        Assert.False(tenant.HasTenant);
    }

    public class UnusedBus : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("The bus must not be used for an event without tenant.");
    }
}
