using System.Collections;
using System.Reflection;
using BuildingBlocks.Domain;
using TaxVision.Tenant.Application.Tenants.Commands;
using Wolverine;

namespace TaxVision.Tenant.Tests.TestSupport;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre los handlers de dominio de
/// Tenant.Application sobre los eventos y junta lo que devuelven.
/// </summary>
internal static class TenantDomainEventReplay
{
    private static readonly MethodInfo[] HandleMethods = typeof(ChangeTenantStatusHandler)
        .Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Handler", StringComparison.Ordinal))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m =>
            m.Name == "Handle"
            && m.GetParameters().FirstOrDefault()?.ParameterType is { } p
            && typeof(IDomainEvent).IsAssignableFrom(p)
        )
        .ToArray();

    public static IReadOnlyList<object> Dispatch(IEnumerable<IDomainEvent> events) =>
        events.ToList().SelectMany(Handle).ToList();

    private static IEnumerable<object> Handle(IDomainEvent domainEvent)
    {
        var method =
            HandleMethods.SingleOrDefault(m => m.GetParameters()[0].ParameterType == domainEvent.GetType())
            ?? throw new InvalidOperationException($"Sin handler de dominio para {domainEvent.GetType().Name}.");

        var result = method.Invoke(null, [domainEvent, new Envelope { CorrelationId = "origin-correlation" }]);
        return result switch
        {
            null => [],
            IEnumerable many and not string => many.Cast<object>(),
            _ => [result],
        };
    }
}
