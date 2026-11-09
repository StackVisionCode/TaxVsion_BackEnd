using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using Wolverine;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos de dominio pendientes de los
/// agregados, corre sus handlers de Subscription.Application y junta lo que devuelven. Así una prueba de handler
/// ve lo emitido (publicado a mano + devuelto por los handlers de dominio).
/// </summary>
internal static class SubscriptionDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(SubscriptionStatusChangedDomainEventHandler)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    /// <summary>Publicado a mano más lo devuelto por los handlers de dominio de <paramref name="roots"/>.</summary>
    public static IReadOnlyList<object> Emitted(CapturingMessageBus bus, params AggregateRoot[] roots) =>
        [.. bus.Published, .. Dispatch(roots)];

    public static IReadOnlyList<object> Dispatch(params AggregateRoot[] roots)
    {
        var emitted = new List<object>();
        foreach (var root in roots)
        {
            var events = root.DomainEvents.ToList();
            root.ClearDomainEvents();
            emitted.AddRange(DispatchEvents(events));
        }
        return emitted;
    }

    /// <summary>Corre los handlers sobre eventos ya sacados (por ejemplo, por el outbox de prueba).</summary>
    public static IReadOnlyList<object> DispatchEvents(IEnumerable<IDomainEvent> events)
    {
        var emitted = new List<object>();
        foreach (var domainEvent in events)
        {
            var method =
                HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
                ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");
            var args = method
                .GetParameters()
                .Select(
                    object (p) =>
                        p.ParameterType == domainEvent.GetType() ? domainEvent
                        : p.ParameterType == typeof(Envelope) ? new Envelope { CorrelationId = "replay-correlation" }
                        : throw new InvalidOperationException($"El replay no sabe resolver {p.ParameterType.Name}.")
                )
                .ToArray();
            var result = method.Invoke(null, args);
            if (result is IEnumerable many and not string)
                emitted.AddRange(many.Cast<object>());
            else if (result is not null)
                emitted.Add(result);
        }
        return emitted;
    }
}
