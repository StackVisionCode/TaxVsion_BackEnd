// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Audit;

namespace TaxVision.PaymentClient.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class PaymentClientLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(PaymentAuditEntry).Assembly;

    protected override Assembly ApplicationAssembly => typeof(WebhookRegistrationErrors).Assembly;

    // Con el punto: "Stripe" a secas también marca la constante SectionName = "Stripe".
    private const string StripeNamespace = "Stripe.";

    // Regla propia: Stripe.net solo en Infrastructure (PayPal se llama por HTTP, sin SDK).
    [Fact]
    public void DomainAndApplication_ShouldNotDependOn_Stripe()
    {
        var violations = LayeringRules
            .FindViolations(DomainAssembly, StripeNamespace, Exemptions)
            .Concat(LayeringRules.FindViolations(ApplicationAssembly, StripeNamespace, Exemptions))
            .ToArray();

        Assert.True(violations.Length == 0, "Dependen de Stripe.net:\n  " + string.Join("\n  ", violations));
    }
}
