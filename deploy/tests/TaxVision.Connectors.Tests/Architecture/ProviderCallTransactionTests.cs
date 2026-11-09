using System.Reflection;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Tests.Architecture;

/// <summary>
/// CON-03 y CON-12: un handler o consumer que habla con un proveedor no mantiene una transacción abierta mientras
/// espera. El puerto se busca también detrás de las clases de apoyo que recibe (CON-13 los movió ahí).
/// </summary>
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

    [Fact]
    public void HandlersThatCallAProvider_RunOutsideTheTransaction()
    {
        var offenders = FindOffenders(typeof(ConnectManualAccountHandler).Assembly.GetTypes())
            .Where(name => !NoTransactionToHold.Contains(name))
            .ToList();

        Assert.Empty(offenders);
    }

    // La regla ve el puerto escondido en una clase de apoyo y respeta [NonTransactional].
    [Fact]
    public void Detection_SeesAProviderPortBehindAHelperClass()
    {
        var offenders = FindOffenders([
            typeof(SampleViaHelperHandler),
            typeof(SampleNonTransactionalHandler),
            typeof(SampleInstanceHandler),
        ]);

        Assert.Equal([nameof(SampleInstanceHandler), nameof(SampleViaHelperHandler)], offenders.Order());
    }

    private static readonly string[] HandlerMethodNames = ["Handle", "HandleAsync", "Consume", "ConsumeAsync"];

    private static List<string> FindOffenders(IEnumerable<Type> types) =>
        types
            .Where(type =>
                type.Name.EndsWith("Handler", StringComparison.Ordinal)
                || type.Name.EndsWith("Consumer", StringComparison.Ordinal)
            )
            .Where(type =>
                type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )
                    .Where(method => HandlerMethodNames.Contains(method.Name))
                    .Where(method => method.GetCustomAttribute<NonTransactionalAttribute>() is null)
                    .Any(method =>
                        method.GetParameters().Any(p => ReachesAProvider(p.ParameterType, []))
                        || (!method.IsStatic && ConstructorReachesAProvider(type, []))
                    )
            )
            .Select(type => type.Name)
            .ToList();

    // El puerto directo, o una clase propia que lo recibe en su constructor (a cualquier profundidad).
    private static bool ReachesAProvider(Type type, HashSet<Type> visited)
    {
        if (ProviderPorts.Contains(type))
            return true;
        if (!type.IsClass || type.Namespace?.StartsWith("TaxVision.Connectors", StringComparison.Ordinal) != true)
            return false;
        return visited.Add(type) && ConstructorReachesAProvider(type, visited);
    }

    private static bool ConstructorReachesAProvider(Type type, HashSet<Type> visited) =>
        type.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .Any(p => ReachesAProvider(p.ParameterType, visited));

    public sealed record SampleCommand;

    public sealed class SampleHelper(IEmailProviderClientFactory providers)
    {
        public IEmailProviderClientFactory Providers { get; } = providers;
    }

    public sealed class SampleOuterHelper(SampleHelper inner)
    {
        public SampleHelper Inner { get; } = inner;
    }

    public static class SampleViaHelperHandler
    {
        public static Task Handle(SampleCommand command, SampleOuterHelper helper) => Task.CompletedTask;
    }

    public static class SampleNonTransactionalHandler
    {
        [NonTransactional]
        public static Task Handle(SampleCommand command, SampleHelper helper) => Task.CompletedTask;
    }

    public sealed class SampleInstanceHandler(SampleHelper helper)
    {
        public SampleHelper Helper { get; } = helper;

        public Task Handle(SampleCommand command) => Task.CompletedTask;
    }
}
