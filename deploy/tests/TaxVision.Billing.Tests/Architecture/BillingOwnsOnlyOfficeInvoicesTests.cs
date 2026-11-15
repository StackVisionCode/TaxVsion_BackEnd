using System.Reflection;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>
/// Billing es el contexto de la oficina facturándole a sus clientes. Los cobros de la plataforma a la oficina
/// (alta, renovaciones, add-ons) viven en PaymentApp con su recibo: Billing no los recibe ni los modela.
/// </summary>
public sealed class BillingOwnsOnlyOfficeInvoicesTests
{
    private static readonly Assembly[] BillingAssemblies =
    [
        typeof(Invoice).Assembly,
        typeof(TaxVision.Billing.Application.Abstractions.IInvoiceRepository).Assembly,
        typeof(TaxVision.Billing.Infrastructure.Persistence.BillingDbContext).Assembly,
        typeof(TaxVision.Billing.Api.Controllers.InvoicesController).Assembly,
    ];

    private static readonly Type[] PlatformOnboardingEvents =
    [
        typeof(OnboardingInvoiceRequestedIntegrationEvent),
        typeof(TenantCreatedForOnboardingIntegrationEvent),
    ];

    [Fact]
    public void No_billing_handler_consumes_platform_onboarding_events()
    {
        var handlers = BillingAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            .Where(method => method.Name is "Handle" or "HandleAsync" or "Consume" or "ConsumeAsync")
            .Where(method => method.GetParameters().Any(p => PlatformOnboardingEvents.Contains(p.ParameterType)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.True(
            handlers.Count == 0,
            "Billing still consumes platform onboarding events: " + string.Join(", ", handlers)
        );
    }

    [Theory]
    [InlineData("OnboardingId")]
    [InlineData("PlanId")]
    [InlineData("SettlementType")]
    [InlineData("PaymentId")]
    public void The_invoice_has_no_platform_charge_fields(string property) =>
        Assert.Null(typeof(Invoice).GetProperty(property, BindingFlags.Public | BindingFlags.Instance));

    [Fact]
    public void Billing_has_no_onboarding_tenant_map() =>
        Assert.DoesNotContain(
            BillingAssemblies.SelectMany(assembly => assembly.GetTypes()),
            type => type.Name.StartsWith("OnboardingTenantMap", StringComparison.Ordinal)
        );
}
