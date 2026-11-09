using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.BuildingBlocks.Testing.Messaging;

/// <summary>
/// T-ARQ de BB-2: todo mensaje con handler propio que el servicio rutea a una cola local va a una cola durable,
/// sea por <c>UseTaxVisionDurableLocalQueues()</c> o por una cola con <c>UseDurableInbox()</c>. Se mide sobre el
/// runtime de Wolverine ya arrancado (las rutas reales), no sobre el código. Cada servicio la hereda con su host
/// de pruebas:
/// <code>
/// public sealed class LocalQueueDurabilityTests(TasksApiFactory factory)
///     : LocalQueueDurabilityRules, IClassFixture&lt;TasksApiFactory&gt;
/// {
///     protected override IServiceProvider Services =&gt; factory.Services;
/// }
/// </code>
/// O llama <see cref="FindNonDurableLocalRoutes"/> desde su propia prueba.
/// </summary>
public abstract class LocalQueueDurabilityRules
{
    /// <summary>Servicios del host del servicio, con Wolverine arrancado.</summary>
    protected abstract IServiceProvider Services { get; }

    /// <summary>
    /// Mensajes que solo se ejecutan con <c>InvokeAsync</c> (en línea, sin cola) y por eso no necesitan una cola
    /// durable. Cada excepción debe llevar su motivo en el servicio.
    /// </summary>
    protected virtual IEnumerable<Type> InvokeOnlyMessages => [];

    [Fact]
    public void LocallyRoutedMessages_UseDurableQueues()
    {
        var offenders = FindNonDurableLocalRoutes(Services.GetRequiredService<IWolverineRuntime>(), InvokeOnlyMessages);

        Assert.True(
            offenders.Count == 0,
            "Mensajes con handler propio que van a una cola local en memoria (se pierden al reiniciar). "
                + "Activar UseTaxVisionDurableLocalQueues() o usar una cola con UseDurableInbox():\n  "
                + string.Join("\n  ", offenders)
        );
    }

    /// <summary>
    /// Rutas locales no durables de los mensajes que el runtime sabe manejar, salvo los de Wolverine y los
    /// <paramref name="exempt"/>. Cada entrada: <c>Tipo -&gt; local://cola (Modo)</c>.
    /// </summary>
    public static IReadOnlyList<string> FindNonDurableLocalRoutes(
        IWolverineRuntime runtime,
        IEnumerable<Type>? exempt = null
    )
    {
        var skip = (exempt ?? []).ToHashSet();
        List<string> offenders = [];

        var handlers =
            (runtime as WolverineRuntime)?.Handlers
            ?? throw new InvalidOperationException("Se esperaba el WolverineRuntime del host.");
        var messageTypes = handlers
            .Chains.Select(chain => chain.MessageType)
            .Where(type => !IsFrameworkType(type) && !skip.Contains(type))
            .Distinct();

        foreach (var type in messageTypes)
        {
            var message = RuntimeHelpers.GetUninitializedObject(type);
            foreach (var envelope in runtime.RoutingFor(type).RouteForPublish(message, null))
            {
                if (envelope.Destination is not { Scheme: "local" } destination)
                    continue;

                var mode = runtime.Endpoints.EndpointFor(destination)?.Mode;
                if (mode != EndpointMode.Durable)
                    offenders.Add($"{type.FullName} -> {destination} ({mode})");
            }
        }

        return offenders.OrderBy(o => o).ToList();
    }

    private static bool IsFrameworkType(Type type) =>
        type.Namespace is { } ns
        && (
            ns.StartsWith("Wolverine", StringComparison.Ordinal) || ns.StartsWith("JasperFx", StringComparison.Ordinal)
        );
}
