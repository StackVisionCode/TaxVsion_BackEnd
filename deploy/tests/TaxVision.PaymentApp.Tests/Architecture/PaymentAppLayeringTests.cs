// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.Audit;

namespace TaxVision.PaymentApp.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class PaymentAppLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(PaymentAuditEntry).Assembly;

    protected override Assembly ApplicationAssembly => typeof(PaymentProviderAttribute).Assembly;

    // Con el punto: "Stripe" a secas también marca constantes propias con ese texto.
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
