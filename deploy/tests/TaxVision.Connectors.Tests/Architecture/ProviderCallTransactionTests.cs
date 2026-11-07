using System.Reflection;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Tests.Architecture;

/// <summary>CON-03 y CON-12: un handler que habla con un proveedor no mantiene una transacción abierta mientras espera.</summary>
public sealed class ProviderCallTransactionTests
{
    private static readonly Type[] ProviderPorts =
    [
        typeof(IEmailProviderClientFactory),
        typeof(IWatchProviderClientFactory),
        typeof(IOAuthProviderClientFactory),
        typeof(IWatchRenewalService),
        typeof(IManualAccountConnectivityValidator),
    ];

    // Sin base de datos, o usan la fábrica solo para leer capacidades: Wolverine no les abre transacción.
    private static readonly HashSet<string> NoTransactionToHold =
    [
        "GetTenantEmailAccountHandler",
        "InitiateOAuthConnectHandler",
        "ListTenantEmailAccountsHandler",
        "TestManualConnectionHandler",
    ];

    // Vistos al hacer CON-12, fuera del ítem. Solo leen: la transacción no bloquea filas, pero retiene la conexión.
    private static readonly Dictionary<string, string> PendingReview = new()
    {
        ["GetMessageAttachmentHandler"] = "Lectura bajo demanda del adjunto en el proveedor.",
        ["GetMessageBodyHandler"] = "Lectura bajo demanda del cuerpo en el proveedor.",
        ["InspectDeliveryReportHandler"] = "Baja el correo crudo y publica por el outbox del handler.",
    };

    [Fact]
    public void HandlersThatCallAProvider_RunOutsideTheTransaction()
    {
        var offenders = typeof(ConnectManualAccountHandler)
            .Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name is "Handle" or "HandleAsync")
            .Where(method => method.GetParameters().Any(p => ProviderPorts.Contains(p.ParameterType)))
            .Where(method => method.GetCustomAttribute<NonTransactionalAttribute>() is null)
            .Select(method => method.DeclaringType!.Name)
            .Where(name => !NoTransactionToHold.Contains(name) && !PendingReview.ContainsKey(name))
            .ToList();

        Assert.Empty(offenders);
    }
}
