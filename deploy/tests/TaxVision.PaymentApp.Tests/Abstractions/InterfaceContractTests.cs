using System.Reflection;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Tests.Abstractions;

/// <summary>
/// Las nuevas abstracciones provider-agnostic (hosted checkout + off-session + mandate) deben
/// vivir en Application y no filtrar nombres de proveedor. Fija el contrato y los records
/// como immutables (init-only) — un setter mutable escaparía el guardrail de inmutabilidad
/// que pide §44.1.
/// </summary>
public sealed class InterfaceContractTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IReceiptDownloadUrlClient).Assembly;

    [Fact]
    public void IHostedCheckoutSessionCreator_exists_in_application()
    {
        Assert.NotNull(ApplicationAssembly.GetType(typeof(IHostedCheckoutSessionCreator).FullName!));
    }

    [Fact]
    public void IOffSessionCharger_exists_in_application()
    {
        Assert.NotNull(ApplicationAssembly.GetType(typeof(IOffSessionCharger).FullName!));
    }

    [Fact]
    public void IOffSessionChargerFactory_exists_in_application()
    {
        Assert.NotNull(ApplicationAssembly.GetType(typeof(IOffSessionChargerFactory).FullName!));
    }

    [Fact]
    public void IMandateCollector_exists_in_application()
    {
        Assert.NotNull(ApplicationAssembly.GetType(typeof(IMandateCollector).FullName!));
    }

    [Fact]
    public void Application_assembly_does_not_reference_provider_sdks()
    {
        // Ningún tipo de Application puede estar en namespaces de SDK de provider — si
        // apareciera, el guardrail del adapter (Infrastructure-only) estaría roto.
        var forbidden = new[] { "Stripe", "PayPalCheckoutSdk", "PayPalHttp", "BraintreeHttp" };
        var violations = ApplicationAssembly
            .GetTypes()
            .Where(type =>
                forbidden.Any(prefix => (type.Namespace ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
            )
            .Select(type => type.FullName!)
            .ToList();
        Assert.True(
            violations.Count == 0,
            "Application contiene tipos de SDK de provider: " + string.Join(", ", violations)
        );
    }

    [Theory]
    [InlineData(typeof(HostedCheckoutCreateRequest))]
    [InlineData(typeof(HostedCheckoutSession))]
    [InlineData(typeof(OffSessionChargeRequest))]
    [InlineData(typeof(OffSessionChargeOutcome))]
    [InlineData(typeof(MandateContext))]
    [InlineData(typeof(MandateReference))]
    public void Records_are_immutable_init_only(Type recordType)
    {
        var mutable = recordType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(prop =>
            {
                var setter = prop.GetSetMethod(nonPublic: true);
                if (setter is null)
                    return false;
                // init-only setters marcan IsExternalInit en modreq del parámetro de retorno.
                var modreqs = setter.ReturnParameter.GetRequiredCustomModifiers();
                var isInitOnly = modreqs.Any(modreq =>
                    modreq.FullName == "System.Runtime.CompilerServices.IsExternalInit"
                );
                return !isInitOnly;
            })
            .Select(prop => prop.Name)
            .ToList();
        Assert.True(mutable.Count == 0, $"{recordType.Name} tiene properties mutables: " + string.Join(", ", mutable));
    }
}
