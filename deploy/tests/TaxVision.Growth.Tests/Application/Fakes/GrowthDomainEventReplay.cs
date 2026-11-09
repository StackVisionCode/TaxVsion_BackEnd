using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Growth.Api.DomainEvents;
using Wolverine;

namespace TaxVision.Growth.Tests.Application.Fakes;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre los handlers de dominio de Growth sobre
/// los eventos y junta lo que devuelven. Un evento sin handler rompe la prueba.
/// </summary>
internal static class GrowthDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(CodeReservationCommittedDomainEventHandler)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    public static IReadOnlyList<object> Dispatch(IEnumerable<IDomainEvent> events)
    {
        var emitted = new List<object>();
        foreach (var domainEvent in events.ToList())
        {
            var method =
                HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
                ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

            object?[] args = method.GetParameters().Length == 1 ? [domainEvent] : [domainEvent, new Envelope()];
            if (method.Invoke(null, args) is { } result)
                emitted.Add(result);
        }
        return emitted;
    }
}
