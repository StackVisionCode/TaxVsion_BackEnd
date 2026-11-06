using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// Lo que hace Wolverine con UseTaxVisionDomainEvents(), en memoria: saca los eventos de dominio pendientes de los
/// agregados, corre sus handlers de PaymentClient.Application y junta lo que devuelven. Así una prueba de handler ve
/// lo emitido por el comando: lo publicado a mano más lo devuelto por los handlers de dominio.
/// </summary>
internal static class DomainEventReplay
{
    public const string EnvelopeCorrelationId = "envelope-correlation-id";

    private static readonly MethodInfo[] HandleMethods = typeof(TenantPaymentSucceededSignal)
        .Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    /// <summary>Lo publicado más lo que devuelven los handlers de dominio de los agregados que rastrea <paramref name="db"/>.</summary>
    public static Task<IReadOnlyList<object>> EmittedAsync(IEnumerable<object> published, PaymentClientDbContext db) =>
        EmittedAsync(
            published,
            new TenantPaymentRepository(db),
            db.ChangeTracker.Entries<AggregateRoot>().Select(entry => entry.Entity).ToList()
        );

    public static async Task<IReadOnlyList<object>> EmittedAsync(
        IEnumerable<object> published,
        ITenantPaymentRepository payments,
        IEnumerable<AggregateRoot> roots
    )
    {
        var emitted = new List<object>(published);
        foreach (var root in roots.ToList())
        {
            var events = root.DomainEvents.ToList();
            root.ClearDomainEvents();
            foreach (var domainEvent in events)
                emitted.AddRange(await HandleAsync(domainEvent, payments));
        }
        return emitted;
    }

    private static async Task<IEnumerable<object>> HandleAsync(IDomainEvent domainEvent, ITenantPaymentRepository payments)
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var args = method.GetParameters().Select(p => Resolve(p.ParameterType, domainEvent, payments)).ToArray();
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

    private static object? Resolve(Type type, IDomainEvent domainEvent, ITenantPaymentRepository payments) =>
        type switch
        {
            _ when type == domainEvent.GetType() => domainEvent,
            _ when type == typeof(ITenantPaymentRepository) => payments,
            _ when type == typeof(Envelope) => new Envelope { CorrelationId = EnvelopeCorrelationId },
            _ when type == typeof(CancellationToken) => CancellationToken.None,
            _ => throw new InvalidOperationException($"El replay no sabe resolver {type.Name}."),
        };
}
