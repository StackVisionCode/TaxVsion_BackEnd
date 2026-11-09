using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Tests.Fakes;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos pendientes de los runs,
/// corre sus handlers y junta lo que devuelven.
/// </summary>
internal static class CampaignDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(StartCampaignRunHandler)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    /// <summary>Despacha los eventos pendientes y deja lo emitido en <paramref name="bus"/>, como si saliera por él.</summary>
    public static void DispatchInto(FakeMessageBus bus, IEnumerable<CampaignRun> runs) =>
        bus.Published.AddRange(Dispatch(runs));

    public static IReadOnlyList<object> Dispatch(IEnumerable<CampaignRun> runs)
    {
        var emitted = new List<object>();
        foreach (var run in runs.ToList())
        {
            var events = run.DomainEvents.ToList();
            run.ClearDomainEvents();
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
                    : p.ParameterType == typeof(Envelope) ? new Envelope { CorrelationId = "test" }
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
