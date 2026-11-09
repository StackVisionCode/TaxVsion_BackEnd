using System.Collections;
using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using TaxVision.Tasks.Application;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos de dominio pendientes
/// de las tareas, corre sus handlers de Tasks.Application y junta lo que devuelven. Así una prueba de handler
/// ve lo emitido por el comando (publicado a mano + devuelto por los handlers de dominio).
/// </summary>
internal static class DomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(AssemblyMarker)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.Name == "Handle" && m.GetParameters().FirstOrDefault()?.ParameterType is { } p && typeof(IDomainEvent).IsAssignableFrom(p))
        .ToArray();

    /// <summary>Publicado a mano por el comando más lo devuelto por los handlers de dominio.</summary>
    public static async Task<IReadOnlyList<object>> EmittedAsync(FakeMessageBus bus, InMemoryTaskRepository tasks) =>
        [.. bus.Published, .. await DispatchAsync(tasks.Stored, tasks)];

    /// <summary>Despacha los eventos pendientes y deja lo emitido en <paramref name="bus"/>, como si saliera por él.</summary>
    public static async Task DispatchIntoAsync(FakeMessageBus bus, InMemoryTaskRepository tasks)
    {
        foreach (var message in await DispatchAsync(tasks.Stored, tasks))
            await bus.PublishAsync(message);
    }

    public static async Task<IReadOnlyList<object>> DispatchAsync(IEnumerable<AggregateRoot> roots, ITaskRepository tasks)
    {
        var emitted = new List<object>();
        foreach (var root in roots.ToList())
        {
            var events = root.DomainEvents.ToList();
            root.ClearDomainEvents();
            foreach (var domainEvent in events)
                emitted.AddRange(await HandleAsync(domainEvent, tasks));
        }
        return emitted;
    }

    /// <summary>Corre los handlers de dominio sobre eventos ya sacados (p. ej. por el outbox de prueba).</summary>
    public static async Task<IReadOnlyList<object>> DispatchEventsAsync(IEnumerable<IDomainEvent> events, ITaskRepository tasks)
    {
        var emitted = new List<object>();
        foreach (var domainEvent in events)
            emitted.AddRange(await HandleAsync(domainEvent, tasks));
        return emitted;
    }

    private static async Task<IEnumerable<object>> HandleAsync(IDomainEvent domainEvent, ITaskRepository tasks)
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var args = method.GetParameters().Select(p => Resolve(p.ParameterType, domainEvent, tasks)).ToArray();
        var result = method.Invoke(null, args);
        if (result is Task task)
        {
            await task;
            result = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        return result switch
        {
            null => [],
            IEnumerable many and not string => many.Cast<object>(),
            _ when result.GetType().FullName == "System.Threading.Tasks.VoidTaskResult" => [],
            _ => [result],
        };
    }

    private static object? Resolve(Type type, IDomainEvent domainEvent, ITaskRepository tasks) =>
        type switch
        {
            _ when type == domainEvent.GetType() => domainEvent,
            _ when type == typeof(ITaskRepository) => tasks,
            _ when type == typeof(ICorrelationContext) => new NoOpCorrelationContext(),
            _ when type == typeof(CancellationToken) => CancellationToken.None,
            _ => throw new InvalidOperationException($"El replay no sabe resolver {type.Name}."),
        };
}
