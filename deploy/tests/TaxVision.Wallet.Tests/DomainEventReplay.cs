using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Wallet.Application;
using Wolverine;

namespace TaxVision.Wallet.Tests;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos de dominio pendientes de
/// los monederos, corre sus handlers de Wallet.Application y junta lo que devuelven.
/// </summary>
internal static class DomainEventReplay
{
    public const string CorrelationId = "replay-correlation";

    private static readonly MethodInfo[] HandleMethods = typeof(AssemblyMarker)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    public static IReadOnlyList<object> Dispatch(IEnumerable<AggregateRoot> roots)
    {
        var emitted = new List<object>();
        foreach (var root in roots.ToList())
        {
            var events = root.DomainEvents.ToList();
            root.ClearDomainEvents();
            foreach (var domainEvent in events)
                emitted.AddRange(Handle(domainEvent));
        }
        return emitted;
    }

    private static IEnumerable<object> Handle(IDomainEvent domainEvent)
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var args = method
            .GetParameters()
            .Select(
                object (p) =>
                    p.ParameterType == domainEvent.GetType() ? domainEvent
                    : p.ParameterType == typeof(Envelope) ? new Envelope { CorrelationId = CorrelationId }
                    : throw new InvalidOperationException($"El replay no sabe resolver {p.ParameterType.Name}.")
            )
            .ToArray();

        return method.Invoke(null, args) switch
        {
            null => [],
            IEnumerable many and not string => many.Cast<object>(),
            var single => [single],
        };
    }
}
