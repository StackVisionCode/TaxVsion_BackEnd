using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Tests.Application;
using Wolverine;

namespace TaxVision.Auth.Tests;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre los handlers de dominio de
/// Auth.Application sobre los eventos y junta lo que devuelven (y lo que publiquen por un bus, si alguno
/// todavía lo hace). Los parámetros se resuelven por tipo desde <c>services</c>.
/// </summary>
internal static class AuthDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(DeactivateUserHandler)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    public static async Task<IReadOnlyList<object>> DispatchAsync(
        IEnumerable<IDomainEvent> events,
        IReadOnlyList<object> services
    )
    {
        var emitted = new List<object>();
        foreach (var domainEvent in events.ToList())
            emitted.AddRange(await HandleAsync(domainEvent, services));
        return emitted;
    }

    /// <summary>Corre el único handler del evento y devuelve lo que emite.</summary>
    public static async Task<IReadOnlyList<object>> HandleAsync(
        IDomainEvent domainEvent,
        IReadOnlyList<object> services
    )
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var bus = new FakeMessageBus();
        var args = method.GetParameters().Select(p => Resolve(p.ParameterType, domainEvent, bus, services)).ToArray();
        var result = method.Invoke(null, args);
        if (result is Task task)
        {
            await task;
            result = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        IEnumerable<object> returned = result switch
        {
            null => [],
            IEnumerable many and not string => many.Cast<object>(),
            _ when result.GetType().FullName == "System.Threading.Tasks.VoidTaskResult" => [],
            _ => [result],
        };
        return [.. returned, .. bus.Published];
    }

    private static object? Resolve(
        Type type,
        IDomainEvent domainEvent,
        IMessageBus bus,
        IReadOnlyList<object> services
    ) =>
        type switch
        {
            _ when type == domainEvent.GetType() => domainEvent,
            _ when type == typeof(CancellationToken) => CancellationToken.None,
            _ when type == typeof(IMessageBus) => bus,
            _ => services.FirstOrDefault(type.IsInstanceOfType)
                ?? throw new InvalidOperationException($"El replay no sabe resolver {type.Name}."),
        };
}
