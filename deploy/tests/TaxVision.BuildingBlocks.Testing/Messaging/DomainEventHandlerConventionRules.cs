using System.Reflection;
using BuildingBlocks.Domain;
using Xunit;

namespace TaxVision.BuildingBlocks.Testing.Messaging;

/// <summary>
/// T-ARQ de BB-1: todo <see cref="IDomainEvent"/> del servicio tiene un handler que Wolverine descubre, para que
/// ningún evento quede muerto en la cola <c>domain-events</c>. Cada servicio que active
/// <c>UseTaxVisionDomainEvents()</c> la hereda con sus ensamblados:
/// <code>
/// public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
/// {
///     protected override IEnumerable&lt;Assembly&gt; DomainAssemblies =&gt; [typeof(TaskItem).Assembly];
///     protected override IEnumerable&lt;Assembly&gt; HandlerAssemblies =&gt; [typeof(CreateTaskHandler).Assembly];
/// }
/// </code>
/// O llama <see cref="FindEventsWithoutHandler"/> desde su propia prueba. También verifica que cada evento
/// sobrevive al sobre (<see cref="DomainEventEnvelopeRoundTrip"/>).
/// </summary>
public abstract class DomainEventHandlerConventionRules
{
    private static readonly HashSet<string> HandlerMethodNames =
    [
        "Handle",
        "Handles",
        "HandleAsync",
        "HandlesAsync",
        "Consume",
        "Consumes",
        "ConsumeAsync",
        "ConsumesAsync",
    ];

    /// <summary>Ensamblados donde se declaran los eventos de dominio (Domain).</summary>
    protected abstract IEnumerable<Assembly> DomainAssemblies { get; }

    /// <summary>Ensamblados donde viven los handlers (Application, a veces Infrastructure).</summary>
    protected abstract IEnumerable<Assembly> HandlerAssemblies { get; }

    [Fact]
    public void EveryDomainEvent_HasAHandler()
    {
        var missing = FindEventsWithoutHandler(
            DomainAssemblies.SelectMany(LoadableTypes),
            HandlerAssemblies.SelectMany(LoadableTypes)
        );

        Assert.True(
            missing.Count == 0,
            "Eventos de dominio sin handler (con BB-1 quedarían muertos en la cola domain-events):\n  "
                + string.Join("\n  ", missing.Select(t => t.FullName))
        );
    }

    // La cola domain-events es durable: tras un reinicio el evento se relee del sobre, no de memoria.
    [Fact]
    public void EveryDomainEvent_SurvivesTheEnvelopeSerializer()
    {
        var broken = DomainEventEnvelopeRoundTrip.FindEventsThatDoNotSurvive(DomainAssemblies.SelectMany(LoadableTypes));

        Assert.True(
            broken.Count == 0,
            "Eventos de dominio que no sobreviven al sobre de Wolverine (fallan o cambian al releerlos tras un reinicio):\n  "
                + string.Join("\n  ", broken)
        );
    }

    /// <summary>
    /// Eventos concretos de <paramref name="candidates"/> que ningún handler de <paramref name="handlerTypes"/>
    /// recibe. Un handler cuenta si Wolverine lo descubre: clase terminada en <c>Handler</c> o <c>Consumer</c>
    /// (en singular) con un método público <c>Handle</c>/<c>Consume</c> (o sus variantes) cuyo primer parámetro
    /// es el evento. Un handler genérico de <see cref="IDomainEvent"/> u <c>object</c> no cuenta.
    /// </summary>
    public static IReadOnlyList<Type> FindEventsWithoutHandler(IEnumerable<Type> candidates, IEnumerable<Type> handlerTypes)
    {
        var handled = handlerTypes
            .Where(IsDiscoverableHandler)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => HandlerMethodNames.Contains(m.Name))
            .Select(m => m.GetParameters().FirstOrDefault()?.ParameterType)
            .Where(p => p is not null && p != typeof(object) && p != typeof(IDomainEvent))
            .Select(p => p!)
            .ToList();

        return candidates
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(t))
            .Where(t => !handled.Any(p => p.IsAssignableFrom(t)))
            .Distinct()
            .OrderBy(t => t.FullName)
            .ToList();
    }

    private static bool IsDiscoverableHandler(Type type) =>
        type.IsClass
        && (type.IsPublic || type.IsNestedPublic)
        && !type.IsGenericTypeDefinition
        && (type.Name.EndsWith("Handler", StringComparison.Ordinal) || type.Name.EndsWith("Consumer", StringComparison.Ordinal));

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
