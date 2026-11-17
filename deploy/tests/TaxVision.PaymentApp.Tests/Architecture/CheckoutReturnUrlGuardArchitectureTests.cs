using System.Reflection;
using TaxVision.PaymentApp.Application.Abstractions.Payments;

namespace TaxVision.PaymentApp.Tests.Architecture;

/// <summary>
/// Todo handler cuyo comando trae una URL de regreso pide el validador de URLs: un checkout nuevo
/// que lo olvide reabre la redirección abierta.
/// </summary>
public sealed class CheckoutReturnUrlGuardArchitectureTests
{
    private const string GuardTypeName = "ICheckoutReturnUrlGuard";

    [Fact]
    public void Every_handler_with_a_return_url_takes_the_guard()
    {
        var handlers = typeof(PaymentProviderAttribute)
            .Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name == "Handle" && m.GetParameters().Length > 0)
            .Where(m => m.GetParameters()[0].ParameterType.GetProperty("SuccessUrl") is not null)
            .ToArray();

        Assert.NotEmpty(handlers);
        var missing = handlers
            .Where(m => m.GetParameters().All(p => p.ParameterType.Name != GuardTypeName))
            .Select(m => m.DeclaringType!.FullName)
            .ToArray();

        Assert.True(missing.Length == 0, "Sin validador de URL de regreso:\n  " + string.Join("\n  ", missing));
    }
}
