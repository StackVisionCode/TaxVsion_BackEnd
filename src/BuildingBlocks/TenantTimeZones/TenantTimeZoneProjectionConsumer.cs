using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>
/// Consumer compartido: proyecta la zona de la oficina desde los eventos de Tenant, ordenado por versión e
/// idempotente. Cada servicio lo engancha con <c>opts.Discovery.IncludeType(typeof(TenantTimeZoneProjectionConsumer))</c>.
/// </summary>
public static class TenantTimeZoneProjectionConsumer
{
    public static Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantTimeZoneStore store,
        ITenantTimeZoneResolver resolver,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggers,
        CancellationToken ct
    ) =>
        Project(evt.NewTenantId, evt.DefaultTimeZoneId, null, evt.OccurredOn, evt, store, resolver, unitOfWork, correlation, loggers, ct);

    public static Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ITenantTimeZoneStore store,
        ITenantTimeZoneResolver resolver,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggers,
        CancellationToken ct
    ) =>
        Project(evt.UpdatedTenantId, evt.DefaultTimeZoneId, evt.TenantVersion, evt.UpdatedAtUtc, evt, store, resolver, unitOfWork, correlation, loggers, ct);

    private static async Task Project(
        Guid tenantId,
        string? timeZoneId,
        long? version,
        DateTime changedAtUtc,
        IntegrationEvent evt,
        ITenantTimeZoneStore store,
        ITenantTimeZoneResolver resolver,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggers,
        CancellationToken ct
    )
    {
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            if (!IanaTimeZone.TryNormalize(timeZoneId, out var zone))
            {
                loggers
                    .CreateLogger(typeof(TenantTimeZoneProjectionConsumer).FullName!)
                    .LogWarning("Tenant {TenantId} sent an invalid time zone ({TimeZoneId}); projection unchanged.", tenantId, timeZoneId);
                return;
            }

            var current = await store.FindAsync(tenantId, ct);
            if (current is null)
                await store.AddAsync(TenantTimeZoneProjection.Create(tenantId, zone, version, changedAtUtc), ct);
            else if (current.IsOlderThan(version, changedAtUtc))
                current.Apply(zone, version, changedAtUtc);
            else
                return; // igual o más nuevo ya aplicado: reordenado o duplicado

            await unitOfWork.SaveChangesAsync(ct);
            resolver.Forget(tenantId);
        }
    }
}
