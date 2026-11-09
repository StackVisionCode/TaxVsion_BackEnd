using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Appointments;
using Wolverine;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos pendientes de las citas,
/// corre sus handlers de Calendar.Application y junta lo que devuelven.
/// </summary>
internal static class CalendarDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(CancelAppointmentHandler)
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
    public static async Task DispatchIntoAsync(
        FakeMessageBus bus,
        IAppointmentRepository appointments,
        params IEnumerable<Appointment> roots
    )
    {
        foreach (var message in await DispatchAsync(roots, appointments))
            await bus.PublishAsync(message);
    }

    public static async Task<IReadOnlyList<object>> DispatchAsync(
        IEnumerable<AggregateRoot> roots,
        IAppointmentRepository appointments
    )
    {
        var emitted = new List<object>();
        foreach (var root in roots.ToList())
        {
            var events = root.DomainEvents.ToList();
            root.ClearDomainEvents();
            foreach (var domainEvent in events)
                emitted.AddRange(await HandleAsync(domainEvent, appointments));
        }

        return emitted;
    }

    private static async Task<IEnumerable<object>> HandleAsync(
        IDomainEvent domainEvent,
        IAppointmentRepository appointments
    )
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var args = method.GetParameters().Select(p => Resolve(p.ParameterType, domainEvent, appointments)).ToArray();
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

    private static object Resolve(Type type, IDomainEvent domainEvent, IAppointmentRepository appointments) =>
        type switch
        {
            _ when type == domainEvent.GetType() => domainEvent,
            _ when type == typeof(IAppointmentRepository) => appointments,
            _ when type == typeof(Envelope) => new Envelope { CorrelationId = "test" },
            _ when type == typeof(CancellationToken) => CancellationToken.None,
            _ => throw new InvalidOperationException($"El replay no sabe resolver {type.Name}."),
        };
}
